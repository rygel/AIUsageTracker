// <copyright file="FlatWindowCardBuilder.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class FlatWindowCardBuilder
{
    internal static IReadOnlyList<ProviderUsage> BuildFlatWindowCards(AgentGroupedProviderUsage provider)
    {
        ProviderMetadataCatalog.TryGet(provider.ProviderId, out var definition);
        var showPrefix = definition?.FlatCardShowProviderPrefix == true;
        var parentDisplayName = showPrefix ? ProviderMetadataCatalog.GetConfiguredDisplayName(provider.ProviderId) : null;
        var isQuotaBased = definition?.IsQuotaBased ?? provider.IsQuotaBased;
        var planType = definition?.PlanType ?? provider.PlanType;

        var cards = new List<ProviderUsage>(provider.Models.Count);
        foreach (var model in provider.Models)
        {
            var modelState = AgentGroupedUsageValueResolver.ResolveModelEffectiveState(model, provider.IsQuotaBased);
            var cardName = showPrefix ? $"{parentDisplayName} ({model.ModelName})" : model.ModelName;
            var description = ResolveCardDescription(provider, modelState.Description);

            cards.Add(new ProviderUsage
            {
                ProviderId = provider.ProviderId,
                CardId = model.ModelId,
                ProviderName = cardName,
                AccountName = provider.AccountName,
                IsAvailable = provider.IsAvailable,
                State = provider.State,
                PlanType = planType,
                IsQuotaBased = isQuotaBased,
                IsCurrencyUsage = definition?.IsCurrencyUsage ?? false,
                RequestsUsed = modelState.UsedPercentage,
                UsedPercent = modelState.UsedPercentage,
                Description = description,
                FetchedAt = provider.FetchedAt,
                NextResetTime = modelState.NextResetTime,
                PeriodDuration = ResolvePeriodDuration(provider.ProviderId),
            });
        }

        return cards;
    }

    internal static TimeSpan? ResolvePeriodDuration(string providerId)
    {
        if (!ProviderMetadataCatalog.TryGet(providerId, out var definition))
        {
            return null;
        }

        if (string.Equals(providerId, definition.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            // Prefer a Rolling window; fall back to the longest available window.
            return (definition.QuotaWindows
                        .FirstOrDefault(window => window.Kind == WindowKind.Rolling && window.PeriodDuration.HasValue)
                    ?? definition.QuotaWindows
                        .Where(window => window.PeriodDuration.HasValue)
                        .OrderByDescending(window => window.PeriodDuration)
                        .FirstOrDefault())
                ?.PeriodDuration;
        }

        // Derived child provider (e.g. "claude-code.sonnet"): try explicit ChildProviderId match first,
        // then fall back to the parent's Rolling window duration so pace/headroom is computed correctly.
        var fromChildProviderId = definition.QuotaWindows
            .FirstOrDefault(window =>
                window.PeriodDuration.HasValue &&
                !string.IsNullOrWhiteSpace(window.ChildProviderId) &&
                string.Equals(window.ChildProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            ?.PeriodDuration;

        return fromChildProviderId
            ?? definition.QuotaWindows
                .FirstOrDefault(window => window.Kind == WindowKind.Rolling && window.PeriodDuration.HasValue)
                ?.PeriodDuration;
    }

    private static string ResolveCardDescription(AgentGroupedProviderUsage provider, string modelDescription)
    {
        if (provider.IsAvailable && provider.State == ProviderUsageState.Available)
        {
            return modelDescription;
        }

        return provider.Description;
    }
}
