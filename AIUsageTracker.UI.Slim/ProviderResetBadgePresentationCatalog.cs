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

        if (ProviderDualQuotaBucketPresentationCatalog.TryGetPresentation(usage, out var dualQuotaBuckets))
        {
            var resetTimes = new[] { dualQuotaBuckets.PrimaryResetTime, dualQuotaBuckets.SecondaryResetTime }
                .Where(reset => reset.HasValue)
                .Select(reset => reset!.Value)
                .Distinct()
                .ToList();
            if (resetTimes.Count > 0)
            {
                return resetTimes;
            }
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
