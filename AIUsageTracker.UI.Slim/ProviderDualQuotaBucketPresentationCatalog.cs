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

        if (!ProviderMetadataCatalog.TryGet(usage.ProviderId ?? string.Empty, out var definition))
        {
            return false;
        }

        var declaredWindows = definition.QuotaWindows
            .Where(window => window.Kind != WindowKind.None)
            .ToList();
        if (declaredWindows.Count == 0)
        {
            return false;
        }

        var orderedBuckets = quotaBuckets
            .Select(detail => new
            {
                Detail = detail,
                DeclaredWindow = FindDeclaredWindow(detail, declaredWindows),
            })
            .Where(item => item.DeclaredWindow != null)
            .OrderBy(item => GetDeclaredWindowOrder(item.DeclaredWindow!, declaredWindows))
            .ToList();
        if (orderedBuckets.Count < 2)
        {
            return false;
        }

        var firstDetail = orderedBuckets[0];
        var secondDetail = orderedBuckets.Skip(1).FirstOrDefault(item =>
            item.DeclaredWindow!.Kind != firstDetail.DeclaredWindow!.Kind);

        if (secondDetail == null)
        {
            return false;
        }

        var parsedFirst = UsageMath.GetEffectiveUsedPercent(firstDetail.Detail);
        var parsedSecond = UsageMath.GetEffectiveUsedPercent(secondDetail.Detail);

        if (!parsedFirst.HasValue || !parsedSecond.HasValue)
        {
            return false;
        }

        presentation = new ProviderDualQuotaBucketPresentation(
            PrimaryLabel: firstDetail.DeclaredWindow!.DualBarLabel,
            PrimaryUsedPercent: parsedFirst.Value,
            PrimaryResetTime: firstDetail.Detail.NextResetTime,
            PrimaryPeriodDuration: firstDetail.DeclaredWindow.PeriodDuration,
            SecondaryLabel: secondDetail.DeclaredWindow!.DualBarLabel,
            SecondaryUsedPercent: parsedSecond.Value,
            SecondaryResetTime: secondDetail.Detail.NextResetTime,
            SecondaryPeriodDuration: secondDetail.DeclaredWindow.PeriodDuration);
        return true;
    }

    private static int GetDeclaredWindowOrder(
        QuotaWindowDefinition declaredWindow,
        IReadOnlyList<QuotaWindowDefinition> declaredWindows)
    {
        for (var idx = 0; idx < declaredWindows.Count; idx++)
        {
            if (declaredWindows[idx].Kind == declaredWindow.Kind)
            {
                return idx;
            }
        }

        return int.MaxValue;
    }

    private static QuotaWindowDefinition? FindDeclaredWindow(
        ProviderUsageDetail detail,
        IReadOnlyList<QuotaWindowDefinition> declaredWindows)
    {
        var detailName = detail.Name?.Trim();
        if (!string.IsNullOrWhiteSpace(detailName))
        {
            var byName = declaredWindows.FirstOrDefault(window =>
                !string.IsNullOrWhiteSpace(window.DetailName) &&
                string.Equals(window.DetailName, detailName, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                return byName;
            }
        }

        if (detail.QuotaBucketKind == WindowKind.None)
        {
            return null;
        }

        var byKind = declaredWindows
            .Where(window => window.Kind == detail.QuotaBucketKind)
            .Take(2)
            .ToList();
        return byKind.Count == 1 ? byKind[0] : null;
    }
}
