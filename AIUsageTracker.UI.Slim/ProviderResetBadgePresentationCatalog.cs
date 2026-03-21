// <copyright file="ProviderResetBadgePresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderResetBadgePresentationCatalog
{
    public static IReadOnlyList<DateTime> ResolveResetTimes(ProviderUsage usage, bool suppressSingleResetFallback)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var detailResetTimes = usage.Details?
            .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
            .Where(detail => detail.QuotaBucketKind != WindowKind.None)
            .Where(detail => detail.NextResetTime.HasValue)
            .Where(detail => UsageMath.GetEffectiveUsedPercent(detail).HasValue)
            .Select(detail => detail.NextResetTime!.Value)
            .Distinct()
            .ToList()
            ?? new List<DateTime>();

        if (detailResetTimes.Count >= 2)
        {
            return detailResetTimes;
        }

        if (suppressSingleResetFallback)
        {
            return Array.Empty<DateTime>();
        }

        return usage.NextResetTime.HasValue
            ? new[] { usage.NextResetTime.Value }
            : Array.Empty<DateTime>();
    }
}
