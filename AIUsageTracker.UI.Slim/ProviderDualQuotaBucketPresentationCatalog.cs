// <copyright file="ProviderDualQuotaBucketPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderDualQuotaBucketPresentationCatalog
{
    public static bool TryGetPresentation(ProviderUsage usage, out ProviderDualQuotaBucketPresentation presentation)
    {
        presentation = null!;

        if (usage.Details?.Any() != true)
        {
            return false;
        }

        var quotaBuckets = usage.Details
            .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
            .Where(detail => detail.QuotaBucketKind != WindowKind.None)
            .ToList();

        if (quotaBuckets.Count < 2)
        {
            return false;
        }

        ProviderMetadataCatalog.TryGet(usage.ProviderId ?? string.Empty, out var definition);
        var declaredWindows = definition?.QuotaWindows.Where(w => w.Kind != WindowKind.None).ToList();
        if (declaredWindows == null || declaredWindows.Count == 0)
        {
            return false;
        }

        var orderedBuckets = quotaBuckets
            .Select(detail => new
            {
                Detail = detail,
                DeclaredWindow = FindMatchingWindow(detail, declaredWindows),
            })
            .Where(x => x.DeclaredWindow != null)
            .OrderBy(x => GetDeclaredWindowOrder(x.DeclaredWindow!, declaredWindows))
            .ToList();

        if (orderedBuckets.Count < 2)
        {
            return false;
        }

        var first = orderedBuckets[0];
        var second = orderedBuckets.Skip(1).FirstOrDefault(x => x.Detail.QuotaBucketKind != first.Detail.QuotaBucketKind);

        if (second == null)
        {
            return false;
        }

        var parsedFirst = UsageMath.GetEffectiveUsedPercent(first.Detail);
        var parsedSecond = UsageMath.GetEffectiveUsedPercent(second.Detail);

        if (!parsedFirst.HasValue || !parsedSecond.HasValue)
        {
            return false;
        }

        presentation = new ProviderDualQuotaBucketPresentation(
            PrimaryLabel: GetWindowLabel(first.Detail, first.DeclaredWindow!),
            PrimaryUsedPercent: parsedFirst.Value,
            PrimaryResetTime: first.Detail.NextResetTime,
            SecondaryLabel: GetWindowLabel(second.Detail, second.DeclaredWindow!),
            SecondaryUsedPercent: parsedSecond.Value,
            SecondaryResetTime: second.Detail.NextResetTime);
        return true;
    }

    private static int GetDeclaredWindowOrder(QuotaWindowDefinition declaredWindow, IReadOnlyList<QuotaWindowDefinition> windows)
    {
        for (var index = 0; index < windows.Count; index++)
        {
            if (ReferenceEquals(windows[index], declaredWindow))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static string GetWindowLabel(ProviderUsageDetail detail, QuotaWindowDefinition declaredWindow)
    {
        // When the detail name carries explicit duration info (e.g. "5h Limit", "1 Day Limit"),
        // extract it as the label. This is more accurate than the static DualBarLabel when multiple
        // window lengths share the same WindowKind (e.g. Kimi Burst covers both 5h and 1-day windows).
        var nameLabel = ExtractDurationLabelFromDetailName(detail.Name);
        if (!string.IsNullOrWhiteSpace(nameLabel))
        {
            return nameLabel;
        }

        return declaredWindow.DualBarLabel;
    }

    private static QuotaWindowDefinition? FindMatchingWindow(
        ProviderUsageDetail detail,
        IReadOnlyList<QuotaWindowDefinition> declaredWindows)
    {
        var detailNameMatch = declaredWindows.FirstOrDefault(window =>
            window.Kind == detail.QuotaBucketKind &&
            window.DetailName != null &&
            string.Equals(window.DetailName, detail.Name, StringComparison.OrdinalIgnoreCase));
        if (detailNameMatch != null)
        {
            return detailNameMatch;
        }

        var sameKindWindows = declaredWindows.Where(window => window.Kind == detail.QuotaBucketKind).ToList();
        return sameKindWindows.Count == 1 ? sameKindWindows[0] : null;
    }

    /// <summary>
    /// Strips the " Limit" suffix from a detail name to produce a compact label.
    /// E.g. "5h Limit" → "5h", "Weekly Limit" → "Weekly". Returns null when the suffix is absent.
    /// </summary>
    private static string? ExtractDurationLabelFromDetailName(string? name)
    {
        const string suffix = " Limit";
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return name[..^suffix.Length].Trim();
    }
}
