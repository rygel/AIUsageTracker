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
        return idx >= 0 ? idx : 99;
    }

    private static string GetWindowLabel(ProviderUsageDetail detail, List<QuotaWindowDefinition>? windows, string fallback)
    {
        var declared = windows?.FirstOrDefault(w => w.Kind == detail.QuotaBucketKind);
        return declared?.DualBarLabel ?? fallback;
    }
}
