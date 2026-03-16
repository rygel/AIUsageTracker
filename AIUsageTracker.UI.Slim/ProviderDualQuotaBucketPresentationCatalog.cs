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

        // Prefer declaration-based ordering and labels; fall back to heuristics.
        ProviderMetadataCatalog.TryGet(usage.ProviderId ?? string.Empty, out var definition);
        var declaredWindows = definition?.QuotaWindows.Where(w => w.Kind != WindowKind.None).ToList();

        var orderedBuckets = quotaBuckets
            .OrderBy(detail => GetWindowOrder(detail.QuotaBucketKind, declaredWindows))
            .ToList();

        var firstDetail = orderedBuckets[0];
        var secondDetail = orderedBuckets.Skip(1).FirstOrDefault(d => d.QuotaBucketKind != firstDetail.QuotaBucketKind);

        if (secondDetail == null)
        {
            return false;
        }

        var parsedFirst = UsageMath.GetEffectiveUsedPercent(firstDetail);
        var parsedSecond = UsageMath.GetEffectiveUsedPercent(secondDetail);

        if (!parsedFirst.HasValue || !parsedSecond.HasValue)
        {
            return false;
        }

        presentation = new ProviderDualQuotaBucketPresentation(
            PrimaryLabel: GetWindowLabel(firstDetail, declaredWindows, "Burst"),
            PrimaryUsedPercent: parsedFirst.Value,
            PrimaryResetTime: firstDetail.NextResetTime,
            SecondaryLabel: GetWindowLabel(secondDetail, declaredWindows, "Rolling"),
            SecondaryUsedPercent: parsedSecond.Value,
            SecondaryResetTime: secondDetail.NextResetTime);
        return true;
    }

    private static int GetWindowOrder(WindowKind kind, List<QuotaWindowDefinition>? windows)
    {
        var idx = windows?.FindIndex(w => w.Kind == kind) ?? -1;
        if (idx >= 0)
        {
            return idx;
        }

        // Default ordering when no declaration exists: short windows (Burst) above long ones (Rolling).
        return kind switch
        {
            WindowKind.Burst => 10,
            WindowKind.Rolling => 20,
            _ => 99,
        };
    }

    private static string GetWindowLabel(ProviderUsageDetail detail, List<QuotaWindowDefinition>? windows, string fallback)
    {
        // When the detail name carries explicit duration info (e.g. "5h Limit", "1 Day Limit"),
        // extract it as the label. This is more accurate than the static DualBarLabel when multiple
        // window lengths share the same WindowKind (e.g. Kimi Burst covers both 5h and 1-day windows).
        var nameLabel = ExtractDurationLabelFromDetailName(detail.Name);
        if (!string.IsNullOrWhiteSpace(nameLabel))
        {
            return nameLabel;
        }

        var declared = windows?.FirstOrDefault(w => w.Kind == detail.QuotaBucketKind);
        return declared?.DualBarLabel ?? fallback;
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
