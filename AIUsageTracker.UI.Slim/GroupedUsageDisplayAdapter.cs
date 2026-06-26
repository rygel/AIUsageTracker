// <copyright file="GroupedUsageDisplayAdapter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

// ARCHITECTURE RULE: The Monitor sends RAW data. It does NOT control rendering.
// The ProviderDefinition class is the single source of truth for all rendering
// decisions (card labels, window kinds, plan type, dual bars, flat vs parent).
// This adapter must NEVER filter, cast, or branch on Monitor data shape to
// decide what to render. No .OfType<T>(), no .Where(WindowKind != ...), no
// hardcoded fallbacks (?? "Burst", ?? false). The definition declares what
// cards exist; this code passes the Monitor's raw values through to them.

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class GroupedUsageDisplayAdapter
{
    public static IReadOnlyList<QuotaProviderUsage> Expand(AgentGroupedUsageSnapshot? snapshot)
    {
        if (snapshot?.Providers == null || snapshot.Providers.Count == 0)
        {
            return Array.Empty<QuotaProviderUsage>();
        }

        var usages = new List<QuotaProviderUsage>(snapshot.Providers.Count * 2);
        foreach (var provider in snapshot.Providers
                     .Where(p => !string.IsNullOrWhiteSpace(p.ProviderId))
                     .OrderBy(p => p.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var definition = ProviderMetadataCatalog.Find(provider.ProviderId);
            if (definition == null)
            {
                continue;
            }

            if (provider.Models.Count > 0)
            {
                usages.AddRange(FlatWindowCardBuilder.BuildFlatWindowCards(provider));
            }
            else
            {
                usages.Add(new WindowedProviderUsage
                {
                    ProviderId = provider.ProviderId,
                    ProviderName = ProviderMetadataCatalog.GetConfiguredDisplayName(provider.ProviderId),
                    AccountName = provider.AccountName,
                    IsAvailable = provider.IsAvailable,
                    State = provider.State,
                    PlanType = definition.PlanType,
                    IsQuotaBased = definition.IsQuotaBased,
                    IsCurrencyUsage = definition.IsCurrencyUsage,
                    RequestsUsed = provider.RequestsUsed,
                    RequestsAvailable = provider.RequestsAvailable,
                    UsedPercent = provider.UsedPercent,
                    Description = provider.Description,
                    FetchedAt = provider.FetchedAt,
                    NextResetTime = provider.NextResetTime,
                    PeriodDuration = FlatWindowCardBuilder.ResolvePeriodDuration(provider.ProviderId),
                    WindowCards = provider.ProviderDetails.Cast<QuotaProviderUsage>().ToList(),
                });
            }
        }

        return usages;
    }
}
