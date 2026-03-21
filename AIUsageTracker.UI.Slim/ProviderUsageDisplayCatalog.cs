// <copyright file="ProviderUsageDisplayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderUsageDisplayCatalog
{
    public static IReadOnlyList<ProviderUsage> BuildMainWindowUsageList(
        IReadOnlyCollection<ProviderUsage> usages,
        IEnumerable<string>? hiddenItemIds = null)
    {
        var hiddenIds = hiddenItemIds ?? Array.Empty<string>();
        var renderPreparation = PrepareForMainWindow(usages, hiddenIds);
        var orderedUsages = ProviderMainWindowOrderingCatalog.OrderForMainWindow(renderPreparation.DisplayableUsages);
        return ExpandSyntheticAggregateChildren(orderedUsages, hiddenIds).ToList();
    }

    public static ProviderRenderPreparation PrepareForMainWindow(
        IReadOnlyCollection<ProviderUsage> usages,
        IEnumerable<string>? hiddenItemIds = null)
    {
        var hiddenSet = hiddenItemIds != null
            ? new HashSet<string>(hiddenItemIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var filteredUsages = usages
            .Where(usage =>
            {
                var id = usage.ProviderId ?? string.Empty;
                return ProviderMetadataCatalog.ShouldShowInMainWindow(id) && !hiddenSet.Contains(id);
            })
            .ToList();

        var collapsedParentProviderIds = ResolveCollapsedParentProviderIds(filteredUsages);

        filteredUsages = filteredUsages
            .Where(ShouldDisplayUsage(collapsedParentProviderIds))
            .ToList();

        filteredUsages = filteredUsages
            .GroupBy(usage => usage.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(SelectPreferredUsage)
            .ToList();

        return new ProviderRenderPreparation(filteredUsages);
    }

    /// <summary>
    /// Expands providers that use synthetic aggregate child rendering into their individual
    /// child cards, filtered by the user's hidden item preferences. Non-aggregate providers
    /// and aggregate providers with no details are yielded as-is, enriched with PeriodDuration
    /// from the catalog so the ViewModel can read it directly without any fallback chain.
    /// </summary>
    public static IEnumerable<ProviderUsage> ExpandSyntheticAggregateChildren(
        IEnumerable<ProviderUsage> usages,
        IEnumerable<string> hiddenItemIds)
    {
        foreach (var usage in usages)
        {
            if (!ProviderMetadataCatalog.ShouldRenderAggregateDetailsInMainWindow(usage.ProviderId ?? string.Empty) ||
                usage.Details?.Any() != true)
            {
                EnrichWithPeriodDuration(usage);
                yield return usage;
                continue;
            }

            var children = CreateAggregateDetailUsages(usage);
            foreach (var child in children)
            {
                EnrichWithPeriodDuration(child);
                if (!hiddenItemIds.Contains(child.ProviderId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>
    /// Sets PeriodDuration on a ProviderUsage from rolling/model-specific quota windows
    /// so downstream colour logic can compute pace directly from Usage.PeriodDuration.
    /// No-ops if the usage already has PeriodDuration set (e.g. synthetic aggregate children).
    /// </summary>
    private static void EnrichWithPeriodDuration(ProviderUsage usage)
    {
        if (usage.PeriodDuration.HasValue) return;

        var canonicalId = ProviderMetadataCatalog.GetCanonicalProviderId(usage.ProviderId ?? string.Empty);
        ProviderMetadataCatalog.TryGet(canonicalId, out var definition);
        var periodWindow = definition?.QuotaWindows
            .FirstOrDefault(w => w.Kind == WindowKind.Rolling && w.PeriodDuration.HasValue)
            ?? definition?.QuotaWindows
                .FirstOrDefault(w => w.Kind == WindowKind.ModelSpecific && w.PeriodDuration.HasValue);

        if (periodWindow != null)
        {
            usage.PeriodDuration = periodWindow.PeriodDuration;
        }
    }

    private static IReadOnlyList<ProviderUsage> CreateAggregateDetailUsages(ProviderUsage parentUsage)
    {
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(parentUsage.ProviderId ?? string.Empty);
        if (!ProviderMetadataCatalog.ShouldRenderAggregateDetailsInMainWindow(canonicalProviderId) ||
            parentUsage.Details?.Any() != true)
        {
            return Array.Empty<ProviderUsage>();
        }

        var planType = parentUsage.PlanType;
        var isQuotaBased = parentUsage.IsQuotaBased;
        ProviderMetadataCatalog.TryGet(canonicalProviderId, out var definition);
        if (definition != null)
        {
            planType = definition.PlanType;
            isQuotaBased = definition.IsQuotaBased;
        }

        var quotaWindows = definition?.QuotaWindows ?? (IReadOnlyList<QuotaWindowDefinition>)Array.Empty<QuotaWindowDefinition>();
        var aggregateDetailDisplaySuffix = ProviderMetadataCatalog.GetAggregateDetailDisplaySuffix(canonicalProviderId);

        return parentUsage.Details
            .Where(detail => detail.DetailType == ProviderUsageDetailType.Model)
            .Select(detail => new
            {
                Detail = detail,
                ModelDisplayName = ResolveAggregateDetailDisplayName(detail),
                DeclaredWindow = FindMatchingWindow(detail, quotaWindows),
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.ModelDisplayName) &&
                !string.IsNullOrWhiteSpace(x.DeclaredWindow?.ChildProviderId))
            .GroupBy(x => x.DeclaredWindow!.ChildProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(x => GetQuotaWindowIndex(quotaWindows, x.DeclaredWindow!))
            .ThenBy(x => x.ModelDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => CreateAggregateDetailUsage(
                canonicalProviderId,
                aggregateDetailDisplaySuffix,
                planType,
                isQuotaBased,
                x.Detail,
                x.ModelDisplayName,
                parentUsage,
                x.DeclaredWindow!))
            .ToList();
    }

    private static HashSet<string> ResolveCollapsedParentProviderIds(IEnumerable<ProviderUsage> usages)
    {
        return usages
            .Where(usage =>
            {
                var providerId = usage.ProviderId ?? string.Empty;
                var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
                return string.Equals(providerId, canonicalProviderId, StringComparison.OrdinalIgnoreCase) &&
                       ProviderMetadataCatalog.ShouldCollapseDerivedChildrenInMainWindow(providerId);
            })
            .Select(usage => usage.ProviderId ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Func<ProviderUsage, bool> ShouldDisplayUsage(IReadOnlySet<string> collapsedParentProviderIds)
    {
        return usage =>
        {
            var providerId = usage.ProviderId ?? string.Empty;
            var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
            var isDerivedChild = !string.Equals(providerId, canonicalProviderId, StringComparison.OrdinalIgnoreCase);
            return !isDerivedChild || !collapsedParentProviderIds.Contains(canonicalProviderId);
        };
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
        ProviderUsage parentUsage,
        QuotaWindowDefinition declaredWindow)
    {
        var effectiveUsed = UsageMath.GetEffectiveUsedPercent(detail);
        var hasRemainingPercent = effectiveUsed.HasValue;
        var effectiveRemaining = !effectiveUsed.HasValue
            ? 0
            : Math.Clamp(100 - effectiveUsed.Value, 0, 100);

        var childProviderId = declaredWindow.ChildProviderId!;

        return new ProviderUsage
        {
            ProviderId = childProviderId,
            ProviderName = $"{modelDisplayName} {aggregateDetailDisplaySuffix}",
            UsedPercent = effectiveUsed ?? 0,
            RequestsUsed = 100.0 - effectiveRemaining,
            RequestsAvailable = 100,
            IsQuotaBased = isQuotaBased,
            PlanType = planType,
            Description = !parentUsage.IsAvailable && !string.IsNullOrWhiteSpace(parentUsage.Description)
                ? parentUsage.Description
                : hasRemainingPercent ? $"{effectiveRemaining:F0}% Remaining" : "Usage unknown",
            NextResetTime = detail.NextResetTime ?? parentUsage.NextResetTime,
            IsAvailable = parentUsage.IsAvailable,
            AuthSource = parentUsage.AuthSource,
            AccountName = parentUsage.AccountName,
            PeriodDuration = declaredWindow.PeriodDuration,
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

    private static QuotaWindowDefinition? FindMatchingWindow(ProviderUsageDetail detail, IReadOnlyList<QuotaWindowDefinition> windows)
    {
        if (windows.Count == 0) return null;

        return windows.FirstOrDefault(w =>
            w.Kind == detail.QuotaBucketKind &&
            w.DetailName != null &&
            string.Equals(w.DetailName, detail.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetQuotaWindowIndex(
        IReadOnlyList<QuotaWindowDefinition> windows,
        QuotaWindowDefinition declaredWindow)
    {
        for (var index = 0; index < windows.Count; index++)
        {
            if (ReferenceEquals(windows[index], declaredWindow))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
