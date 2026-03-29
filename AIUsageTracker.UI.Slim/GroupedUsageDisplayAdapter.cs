// <copyright file="GroupedUsageDisplayAdapter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.UI.Slim;

internal static class GroupedUsageDisplayAdapter
{
    public static IReadOnlyList<ProviderUsage> Expand(AgentGroupedUsageSnapshot? snapshot)
    {
        if (snapshot?.Providers == null || snapshot.Providers.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        var usages = new List<ProviderUsage>(snapshot.Providers.Count * 2);
        foreach (var provider in snapshot.Providers
                     .Where(provider => !string.IsNullOrWhiteSpace(provider.ProviderId))
                     .OrderBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            if (provider.Models.Count > 0)
            {
                usages.AddRange(FlatWindowCardBuilder.BuildFlatWindowCards(provider));
            }
            else
            {
                usages.Add(LegacyParentCardBuilder.Build(provider));
            }
        }

        return usages;
    }
}
