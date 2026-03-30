// <copyright file="GroupedUsageDisplayAdapter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

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
            var definition = ProviderMetadataCatalog.Find(provider.ProviderId);
            if (definition?.FamilyMode == ProviderFamilyMode.FlatWindowCards && provider.Models.Count > 0)
            {
                // Pure flat-card providers: each model is an independent card.
                usages.AddRange(FlatWindowCardBuilder.BuildFlatWindowCards(provider));
            }
            else if (definition?.FamilyMode == ProviderFamilyMode.DynamicChildProviderRows && provider.Models.Count > 0)
            {
                // Parent card (dual-bar capable) + child model cards.
                usages.Add(LegacyParentCardBuilder.Build(provider));
                usages.AddRange(FlatWindowCardBuilder.BuildFlatWindowCards(provider));
            }
            else if (provider.Models.Count > 0)
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
