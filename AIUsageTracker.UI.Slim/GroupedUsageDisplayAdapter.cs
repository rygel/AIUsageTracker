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
            var windowCards = provider.ProviderDetails
                .Where(d => d.WindowKind != WindowKind.None)
                .ToList();

            var parentUsage = new ProviderUsage
            {
                ProviderId = provider.ProviderId,
                ProviderName = ProviderMetadataCatalog.GetConfiguredDisplayName(provider.ProviderId),
                AccountName = provider.AccountName,
                IsAvailable = provider.IsAvailable,
                PlanType = provider.PlanType,
                IsQuotaBased = provider.IsQuotaBased,
                RequestsUsed = provider.RequestsUsed,
                RequestsAvailable = provider.RequestsAvailable,
                UsedPercent = provider.UsedPercent,
                Description = provider.Description,
                FetchedAt = provider.FetchedAt,
                NextResetTime = provider.NextResetTime,
                PeriodDuration = ResolvePeriodDuration(provider.ProviderId),
                WindowCards = windowCards.Count > 0 ? windowCards : null,
            };

            usages.Add(parentUsage);
            usages.AddRange(BuildVisibleDerivedRows(provider, parentUsage));
        }

        return usages;
    }

    private static IReadOnlyList<ProviderUsage> BuildVisibleDerivedRows(
        AgentGroupedProviderUsage provider,
        ProviderUsage parentUsage)
    {
        if (provider.Models.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        var orderedModels = provider.Models
            .Where(model => !string.IsNullOrWhiteSpace(model.ModelName))
            .OrderBy(model => model.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orderedModels.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        var assignments = ProviderDerivedModelAssignmentResolver.Resolve(provider.ProviderId, orderedModels);
        if (assignments.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        var childRows = new List<ProviderUsage>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var model = assignment.Model;
            var modelState = ResolveModelState(model, provider.IsQuotaBased);

            var childWindowCards = BuildWindowCardsFromQuotaBuckets(
                assignment.ProviderId,
                model.QuotaBuckets,
                provider.IsQuotaBased);

            childRows.Add(new ProviderUsage
            {
                ProviderId = assignment.ProviderId,
                ProviderName = ProviderMetadataCatalog.ResolveDisplayLabel(
                    assignment.ProviderId,
                    ProviderMetadataCatalog.GetDerivedModelDisplayName(provider.ProviderId, model.ModelName)),
                AccountName = parentUsage.AccountName,
                IsAvailable = parentUsage.IsAvailable,
                PlanType = parentUsage.PlanType,
                IsQuotaBased = parentUsage.IsQuotaBased,
                RequestsUsed = modelState.UsedPercentage,
                RequestsAvailable = 100,
                UsedPercent = modelState.UsedPercentage,
                Description = modelState.Description,
                FetchedAt = parentUsage.FetchedAt,
                NextResetTime = modelState.NextResetTime,
                PeriodDuration = ResolvePeriodDuration(assignment.ProviderId),
                WindowCards = childWindowCards.Count > 0 ? childWindowCards : null,
            });
        }

        return childRows;
    }

    private static (double UsedPercentage, double RemainingPercentage, string Description, DateTime? NextResetTime) ResolveModelState(
        AgentGroupedModelUsage model,
        bool parentIsQuotaBased)
    {
        return AgentGroupedUsageValueResolver.ResolveModelEffectiveState(model, parentIsQuotaBased);
    }

    private static List<ProviderUsage> BuildWindowCardsFromQuotaBuckets(
        string providerId,
        IReadOnlyList<AgentGroupedQuotaBucketUsage> quotaBuckets,
        bool parentIsQuotaBased)
    {
        var windowBuckets = quotaBuckets
            .Where(b => b.QuotaBucketKind == WindowKind.Burst || b.QuotaBucketKind == WindowKind.Rolling)
            .ToList();

        if (windowBuckets.Count == 0)
        {
            return new List<ProviderUsage>(0);
        }

        var cards = new List<ProviderUsage>(windowBuckets.Count);
        foreach (var bucket in windowBuckets)
        {
            cards.Add(new ProviderUsage
            {
                ProviderId = providerId,
                Name = bucket.BucketName,
                WindowKind = bucket.QuotaBucketKind,
                UsedPercent = AgentGroupedUsageValueResolver.ResolveBucketUsedPercentage(bucket, parentIsQuotaBased),
                NextResetTime = bucket.NextResetTime,
            });
        }

        return cards;
    }

    private static TimeSpan? ResolvePeriodDuration(string providerId)
    {
        if (!ProviderMetadataCatalog.TryGet(providerId, out var definition))
        {
            return null;
        }

        if (string.Equals(providerId, definition.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return definition.QuotaWindows
                .FirstOrDefault(window => window.Kind == WindowKind.Rolling && window.PeriodDuration.HasValue)
                ?.PeriodDuration;
        }

        return definition.QuotaWindows
            .FirstOrDefault(window =>
                window.PeriodDuration.HasValue &&
                !string.IsNullOrWhiteSpace(window.ChildProviderId) &&
                string.Equals(window.ChildProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            ?.PeriodDuration;
    }
}
