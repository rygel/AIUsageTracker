// <copyright file="ProviderUsageDisplayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderUsageDisplayCatalog
{
    public static ProviderRenderPreparation PrepareForMainWindow(
        IReadOnlyCollection<ProviderUsage> usages,
        AgentProviderCapabilitiesSnapshot? capabilities = null)
    {
        var filteredUsages = usages
            .Where(usage => ProviderCapabilityCatalog.ShouldShowInMainWindow(usage.ProviderId ?? string.Empty, capabilities))
            .ToList();
        var hasAntigravityParent = filteredUsages.Any(usage => IsAntigravityParent(usage, capabilities));
        var collapsedParentProviderIds = ResolveCollapsedParentProviderIds(filteredUsages, capabilities);

        filteredUsages = filteredUsages
            .Where(ShouldDisplayUsage(collapsedParentProviderIds, capabilities))
            .GroupBy(usage => usage.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return new ProviderRenderPreparation(filteredUsages, hasAntigravityParent);
    }

    public static IReadOnlyList<ProviderUsage> CreateAntigravityModelUsages(ProviderUsage parentUsage)
    {
        if (parentUsage.Details?.Any() != true)
        {
            return Array.Empty<ProviderUsage>();
        }

        return parentUsage.Details
            .Select(detail => new { Detail = detail, ModelDisplayName = ResolveAntigravityModelDisplayName(detail) })
            .Where(x => !string.IsNullOrWhiteSpace(x.ModelDisplayName) && !x.ModelDisplayName.StartsWith("[", StringComparison.Ordinal))
            .GroupBy(x => x.ModelDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(x => x.ModelDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => CreateAntigravityModelUsage(x.Detail, x.ModelDisplayName, parentUsage))
            .ToList();
    }

    private static HashSet<string> ResolveCollapsedParentProviderIds(
        IEnumerable<ProviderUsage> usages,
        AgentProviderCapabilitiesSnapshot? capabilities)
    {
        return usages
            .Where(usage =>
            {
                var providerId = usage.ProviderId ?? string.Empty;
                var canonicalProviderId = ProviderCapabilityCatalog.GetCanonicalProviderId(providerId, capabilities);
                return string.Equals(providerId, canonicalProviderId, StringComparison.OrdinalIgnoreCase) &&
                       ProviderCapabilityCatalog.ShouldCollapseDerivedChildrenInMainWindow(providerId, capabilities);
            })
            .Select(usage => usage.ProviderId ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Func<ProviderUsage, bool> ShouldDisplayUsage(
        IReadOnlySet<string> collapsedParentProviderIds,
        AgentProviderCapabilitiesSnapshot? capabilities)
    {
        return usage =>
        {
            var providerId = usage.ProviderId ?? string.Empty;
            var canonicalProviderId = ProviderCapabilityCatalog.GetCanonicalProviderId(providerId, capabilities);
            var isDerivedChild = !string.Equals(providerId, canonicalProviderId, StringComparison.OrdinalIgnoreCase);
            return !isDerivedChild || !collapsedParentProviderIds.Contains(canonicalProviderId);
        };
    }

    private static bool IsAntigravityParent(ProviderUsage usage, AgentProviderCapabilitiesSnapshot? capabilities)
    {
        return ProviderCapabilityCatalog.ShouldRenderAggregateDetailsInMainWindow(usage.ProviderId ?? string.Empty, capabilities);
    }

    private static ProviderUsage CreateAntigravityModelUsage(
        ProviderUsageDetail detail,
        string modelDisplayName,
        ProviderUsage parentUsage)
    {
        var remainingPercent = UsageMath.ParsePercent(detail.Used);
        var hasRemainingPercent = remainingPercent.HasValue;
        var effectiveRemaining = remainingPercent ?? 0;

        return new ProviderUsage
        {
            ProviderId = $"antigravity.{modelDisplayName.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal)}",
            ProviderName = $"{modelDisplayName} [Antigravity]",
            RequestsPercentage = effectiveRemaining,
            RequestsUsed = 100.0 - effectiveRemaining,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = hasRemainingPercent ? $"{effectiveRemaining:F0}% Remaining" : "Usage unknown",
            NextResetTime = detail.NextResetTime,
            IsAvailable = parentUsage.IsAvailable,
            AuthSource = parentUsage.AuthSource,
            AccountName = parentUsage.AccountName,
        };
    }

    private static string ResolveAntigravityModelDisplayName(ProviderUsageDetail detail)
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
