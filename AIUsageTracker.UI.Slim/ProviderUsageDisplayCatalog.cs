// <copyright file="ProviderUsageDisplayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderUsageDisplayCatalog
{
    public static ProviderRenderPreparation PrepareForMainWindow(IReadOnlyCollection<ProviderUsage> usages)
    {
        var filteredUsages = usages
            .Where(usage => ProviderCapabilityCatalog.ShouldShowInMainWindow(usage.ProviderId ?? string.Empty))
            .ToList();
        var hasAggregateParent = filteredUsages.Any(IsAggregateParent);
        var collapsedParentProviderIds = ResolveCollapsedParentProviderIds(filteredUsages);

        filteredUsages = filteredUsages
            .Where(ShouldDisplayUsage(collapsedParentProviderIds))
            .ToList();

        filteredUsages = filteredUsages
            .GroupBy(usage => usage.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(SelectPreferredUsage)
            .ToList();

        return new ProviderRenderPreparation(filteredUsages, hasAggregateParent);
    }

    public static IReadOnlyList<ProviderUsage> CreateAggregateDetailUsages(ProviderUsage parentUsage)
    {
        var canonicalProviderId = ProviderCapabilityCatalog.GetCanonicalProviderId(parentUsage.ProviderId ?? string.Empty);
        if (!ProviderCapabilityCatalog.ShouldRenderAggregateDetailsInMainWindow(canonicalProviderId) ||
            parentUsage.Details?.Any() != true)
        {
            return Array.Empty<ProviderUsage>();
        }

        var planType = parentUsage.PlanType;
        var isQuotaBased = parentUsage.IsQuotaBased;
        if (ProviderMetadataCatalog.TryGetUsageSemantics(canonicalProviderId, out var configuredPlanType, out var configuredIsQuotaBased))
        {
            planType = configuredPlanType;
            isQuotaBased = configuredIsQuotaBased;
        }

        var aggregateDetailDisplaySuffix = ProviderMetadataCatalog.GetAggregateDetailDisplaySuffix(canonicalProviderId);

        return parentUsage.Details
            .Select(detail => new { Detail = detail, ModelDisplayName = ResolveAggregateDetailDisplayName(detail) })
            .Where(x => !string.IsNullOrWhiteSpace(x.ModelDisplayName) && !x.ModelDisplayName.StartsWith("[", StringComparison.Ordinal))
            .GroupBy(x => x.ModelDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(x => x.ModelDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => CreateAggregateDetailUsage(
                canonicalProviderId,
                aggregateDetailDisplaySuffix,
                planType,
                isQuotaBased,
                x.Detail,
                x.ModelDisplayName,
                parentUsage))
            .ToList();
    }

    private static HashSet<string> ResolveCollapsedParentProviderIds(IEnumerable<ProviderUsage> usages)
    {
        return usages
            .Where(usage =>
            {
                var providerId = usage.ProviderId ?? string.Empty;
                var canonicalProviderId = ProviderCapabilityCatalog.GetCanonicalProviderId(providerId);
                return string.Equals(providerId, canonicalProviderId, StringComparison.OrdinalIgnoreCase) &&
                       ProviderCapabilityCatalog.ShouldCollapseDerivedChildrenInMainWindow(providerId);
            })
            .Select(usage => usage.ProviderId ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Func<ProviderUsage, bool> ShouldDisplayUsage(IReadOnlySet<string> collapsedParentProviderIds)
    {
        return usage =>
        {
            var providerId = usage.ProviderId ?? string.Empty;
            var canonicalProviderId = ProviderCapabilityCatalog.GetCanonicalProviderId(providerId);
            var isDerivedChild = !string.Equals(providerId, canonicalProviderId, StringComparison.OrdinalIgnoreCase);
            return !isDerivedChild || !collapsedParentProviderIds.Contains(canonicalProviderId);
        };
    }

    private static bool IsAggregateParent(ProviderUsage usage)
    {
        return ProviderCapabilityCatalog.ShouldRenderAggregateDetailsInMainWindow(usage.ProviderId ?? string.Empty);
    }

    private static ProviderUsage SelectPreferredUsage(IGrouping<string, ProviderUsage> group)
    {
        return group
            .OrderByDescending(GetSelectionScore)
            .ThenByDescending(usage => usage.FetchedAt)
            .First();
    }

    private static int GetSelectionScore(ProviderUsage usage)
    {
        var score = 0;
        if (usage.IsAvailable)
        {
            score += 1000;
        }

        if (usage.HttpStatus is >= 200 and < 300)
        {
            score += 100;
        }

        if (usage.Details?.Count > 0)
        {
            score += usage.Details.Count;
            score += usage.Details.Count(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow) * 50;
        }

        if (usage.NextResetTime.HasValue)
        {
            score += 10;
        }

        return score;
    }

    private static ProviderUsage CreateAggregateDetailUsage(
        string canonicalProviderId,
        string aggregateDetailDisplaySuffix,
        PlanType planType,
        bool isQuotaBased,
        ProviderUsageDetail detail,
        string modelDisplayName,
        ProviderUsage parentUsage)
    {
        var effectiveUsed = UsageMath.GetEffectiveUsedPercent(detail, parentIsQuota: true);
        var hasRemainingPercent = effectiveUsed.HasValue;
        var effectiveRemaining = !effectiveUsed.HasValue
            ? 0
            : Math.Clamp(100 - effectiveUsed.Value, 0, 100);

        return new ProviderUsage
        {
            ProviderId = $"{canonicalProviderId}.{modelDisplayName.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal)}",
            ProviderName = $"{modelDisplayName} {aggregateDetailDisplaySuffix}",
            RequestsPercentage = effectiveRemaining,
            RequestsUsed = 100.0 - effectiveRemaining,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = isQuotaBased,
            PlanType = planType,
            Description = !parentUsage.IsAvailable && !string.IsNullOrWhiteSpace(parentUsage.Description)
                ? parentUsage.Description
                : hasRemainingPercent ? $"{effectiveRemaining:F0}% Remaining" : "Usage unknown",
            NextResetTime = detail.NextResetTime,
            IsAvailable = parentUsage.IsAvailable,
            AuthSource = parentUsage.AuthSource,
            AccountName = parentUsage.AccountName,
        };
    }

    private static string ResolveAggregateDetailDisplayName(ProviderUsageDetail detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.Name))
        {
            return detail.Name.Trim();
        }

        return string.IsNullOrWhiteSpace(detail.ModelName)
            ? string.Empty
            : detail.ModelName.Trim();
    }
}
