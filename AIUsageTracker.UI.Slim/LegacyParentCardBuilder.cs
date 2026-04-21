// <copyright file="LegacyParentCardBuilder.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class LegacyParentCardBuilder
{
    internal static ProviderUsage Build(AgentGroupedProviderUsage provider)
    {
        var definition = ProviderMetadataCatalog.Find(provider.ProviderId);
        var windowCards = provider.ProviderDetails
            .Where(d => d.WindowKind != WindowKind.None)
            .ToList();

        return new ProviderUsage
        {
            ProviderId = provider.ProviderId,
            ProviderName = ProviderMetadataCatalog.GetConfiguredDisplayName(provider.ProviderId),
            AccountName = provider.AccountName,
            IsAvailable = provider.IsAvailable,
            State = provider.State,
            PlanType = definition?.PlanType ?? provider.PlanType,
            IsQuotaBased = definition?.IsQuotaBased ?? provider.IsQuotaBased,
            IsCurrencyUsage = definition?.IsCurrencyUsage ?? false,
            RequestsUsed = provider.RequestsUsed,
            RequestsAvailable = provider.RequestsAvailable,
            UsedPercent = provider.UsedPercent,
            Description = provider.Description,
            FetchedAt = provider.FetchedAt,
            NextResetTime = provider.NextResetTime,
            PeriodDuration = FlatWindowCardBuilder.ResolvePeriodDuration(provider.ProviderId),
            WindowCards = windowCards.Count > 0 ? windowCards : null,
        };
    }
}
