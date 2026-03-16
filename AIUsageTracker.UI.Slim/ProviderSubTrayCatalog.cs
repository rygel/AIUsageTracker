// <copyright file="ProviderSubTrayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSubTrayCatalog
{
    public static IReadOnlyList<ProviderUsageDetail> GetEligibleDetails(ProviderUsage? usage)
    {
        if (usage?.Details == null)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        if (ProviderMetadataCatalog.HasDisplayableDerivedProviders(usage.ProviderId ?? string.Empty))
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        return usage.Details
            .Where(detail => ProviderSubDetailPresentationCatalog.IsEligibleDetail(detail, includeRateLimit: false))
            .GroupBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
