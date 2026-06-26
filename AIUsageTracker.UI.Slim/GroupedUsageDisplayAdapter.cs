// <copyright file="GroupedUsageDisplayAdapter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

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
                     .Where(provider => !string.IsNullOrWhiteSpace(provider.ProviderId))
                     .OrderBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            if (provider.Models.Count > 0)
            {
                usages.AddRange(FlatWindowCardBuilder.BuildFlatWindowCards(provider));
            }
            else
            {
                var definition = ProviderMetadataCatalog.Find(provider.ProviderId);
                if (definition == null)
                {
                    continue;
                }

                var windowCards = provider.ProviderDetails
                    .OfType<QuotaProviderUsage>()
                    .Where(d => d.WindowKind != WindowKind.None)
                    .ToList();

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
                    WindowCards = windowCards.Count > 0 ? windowCards : null,
                });
            }
        }

        return usages;
    }
}
