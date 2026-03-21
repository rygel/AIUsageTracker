// <copyright file="MainWindowRuntimeLogic.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static partial class MainWindowRuntimeLogic
{
    public static (bool ShouldTriggerRefresh, double SecondsSinceLastRefresh) CreatePollingRefreshDecision(
        DateTime lastRefreshTrigger,
        DateTime now,
        double refreshCooldownSeconds)
    {
        var secondsSinceLastRefresh = (now - lastRefreshTrigger).TotalSeconds;
        var shouldTriggerRefresh = secondsSinceLastRefresh >= refreshCooldownSeconds;
        return (shouldTriggerRefresh, secondsSinceLastRefresh);
    }

    public static bool ShouldRefreshTrayConfigs(
        bool hasCachedConfigs,
        DateTime lastRefreshUtc,
        DateTime nowUtc,
        TimeSpan refreshInterval)
    {
        return !hasCachedConfigs || (nowUtc - lastRefreshUtc) >= refreshInterval;
    }

    public static string FormatMonitorOfflineStatus(DateTime lastMonitorUpdate, DateTime now)
    {
        if (lastMonitorUpdate == DateTime.MinValue)
        {
            return "Monitor offline — no data received yet";
        }

        var elapsed = now - lastMonitorUpdate;
        var ago = elapsed.TotalSeconds < 60
            ? $"{(int)elapsed.TotalSeconds}s ago"
            : elapsed.TotalHours < 1
                ? $"{(int)elapsed.TotalMinutes}m ago"
                : $"{(int)elapsed.TotalHours}h ago";

        return $"Monitor offline — last sync {ago}";
    }

    public static bool GetSectionIsCollapsed(AppPreferences preferences, bool isQuotaBased)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return isQuotaBased ? preferences.IsPlansAndQuotasCollapsed : preferences.IsPayAsYouGoCollapsed;
    }

    public static void SetSectionIsCollapsed(AppPreferences preferences, bool isQuotaBased, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (isQuotaBased)
        {
            preferences.IsPlansAndQuotasCollapsed = isCollapsed;
            return;
        }

        preferences.IsPayAsYouGoCollapsed = isCollapsed;
    }

    public static ProviderRenderPlan BuildProviderRenderPlan(
        IReadOnlyCollection<ProviderUsage> usages,
        IEnumerable<string>? hiddenProviderItemIds)
    {
        ArgumentNullException.ThrowIfNull(usages);

        if (usages.Count == 0)
        {
            return new ProviderRenderPlan(
                RawCount: 0,
                RenderedCount: 0,
                Message: "No provider data available.",
                Sections: Array.Empty<ProviderSectionLayout>());
        }

        var expandedUsages = BuildMainWindowUsageList(usages, hiddenProviderItemIds);
        if (expandedUsages.Count == 0)
        {
            return new ProviderRenderPlan(
                RawCount: usages.Count,
                RenderedCount: 0,
                Message: "Data received, but no displayable providers were found.",
                Sections: Array.Empty<ProviderSectionLayout>());
        }

        var sections = BuildProviderSectionLayouts(expandedUsages);
        return new ProviderRenderPlan(
            RawCount: usages.Count,
            RenderedCount: expandedUsages.Count,
            Message: null,
            Sections: sections);
    }

    public static IReadOnlyList<ProviderUsage> BuildMainWindowUsageList(
        IReadOnlyCollection<ProviderUsage> usages,
        IEnumerable<string>? hiddenItemIds = null)
    {
        var hiddenIds = hiddenItemIds ?? Array.Empty<string>();
        var renderPreparation = PrepareForMainWindow(usages, hiddenIds);
        var orderedUsages = OrderForMainWindow(renderPreparation);
        return ExpandSyntheticAggregateChildren(orderedUsages, hiddenIds).ToList();
    }

    public static IReadOnlyList<ProviderUsage> PrepareForMainWindow(
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
                return (ProviderMetadataCatalog.Find(id)?.ShowInMainWindow ?? false) && !hiddenSet.Contains(id);
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

        return filteredUsages;
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
            var expandCanonicalId = ProviderMetadataCatalog.GetCanonicalProviderId(usage.ProviderId ?? string.Empty);
            var expandDef = ProviderMetadataCatalog.Find(expandCanonicalId);
            if (!(expandDef != null && string.Equals(expandCanonicalId, expandDef.ProviderId, StringComparison.OrdinalIgnoreCase) && expandDef.RenderDetailsAsSyntheticChildrenInMainWindow) ||
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

    public static IReadOnlyList<ProviderSectionLayout> BuildProviderSectionLayouts(IReadOnlyList<ProviderUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(usages);
        if (usages.Count == 0)
        {
            return Array.Empty<ProviderSectionLayout>();
        }

        var sections = new List<ProviderSectionLayout>();
        bool? currentIsQuota = null;
        List<ProviderUsage>? currentItems = null;

        foreach (var usage in usages)
        {
            if (currentIsQuota != usage.IsQuotaBased || currentItems == null)
            {
                currentIsQuota = usage.IsQuotaBased;
                currentItems = new List<ProviderUsage>();
                sections.Add(new ProviderSectionLayout(currentIsQuota.Value, currentItems));
            }

            currentItems.Add(usage);
        }

        return sections;
    }

    internal static IEnumerable<ProviderUsage> OrderForMainWindow(IEnumerable<ProviderUsage> usages)
    {
        return usages
            .OrderByDescending(usage => usage.IsQuotaBased)
            .ThenBy(
                usage => GetFamilyDisplayName(usage),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                usage => ProviderMetadataCatalog.ResolveDisplayLabel(usage),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.ProviderId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets PeriodDuration on a ProviderUsage from rolling/model-specific quota windows
    /// so downstream colour logic can compute pace directly from Usage.PeriodDuration.
    /// No-ops if the usage already has PeriodDuration set (e.g. synthetic aggregate children).
    /// </summary>
    private static void EnrichWithPeriodDuration(ProviderUsage usage)
    {
        if (usage.PeriodDuration.HasValue)
        {
            return;
        }

        var providerId = usage.ProviderId ?? string.Empty;
        if (!ProviderMetadataCatalog.TryGet(providerId, out var definition))
        {
            return;
        }

        QuotaWindowDefinition? matchedWindow;
        if (string.Equals(providerId, definition.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            matchedWindow = definition.QuotaWindows
                .FirstOrDefault(w => w.Kind == WindowKind.Rolling && w.PeriodDuration.HasValue);
        }
        else
        {
            matchedWindow = definition.QuotaWindows
                .FirstOrDefault(w =>
                    w.PeriodDuration.HasValue &&
                    !string.IsNullOrWhiteSpace(w.ChildProviderId) &&
                    string.Equals(w.ChildProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        }

        if (matchedWindow?.PeriodDuration is { } duration)
        {
            usage.PeriodDuration = duration;

            // Ensure NextResetTime matches the rolling window, not the burst window.
            // Providers often set the parent's NextResetTime from the burst (5h) window,
            // which makes pace projection nonsensical against a 7-day PeriodDuration.
            if (usage.Details != null && matchedWindow.Kind == WindowKind.Rolling)
            {
                var rollingDetail = usage.Details.FirstOrDefault(d =>
                    d.QuotaBucketKind == WindowKind.Rolling && d.NextResetTime.HasValue);
                if (rollingDetail != null)
                {
                    usage.NextResetTime = rollingDetail.NextResetTime;
                }
            }
        }
    }

    private static IReadOnlyList<ProviderUsage> CreateAggregateDetailUsages(ProviderUsage parentUsage)
    {
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(parentUsage.ProviderId ?? string.Empty);
        var aggDef = ProviderMetadataCatalog.Find(canonicalProviderId);
        if (!(aggDef != null && string.Equals(canonicalProviderId, aggDef.ProviderId, StringComparison.OrdinalIgnoreCase) && aggDef.RenderDetailsAsSyntheticChildrenInMainWindow) ||
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
                       (ProviderMetadataCatalog.Find(providerId)?.CollapseDerivedChildrenInMainWindow ?? false);
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

    private static string GetFamilyDisplayName(ProviderUsage usage)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
        return ProviderMetadataCatalog.GetConfiguredDisplayName(canonicalProviderId);
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
        if (windows.Count == 0)
        {
            return null;
        }

        var detailName = detail.Name?.Trim();
        if (string.IsNullOrWhiteSpace(detailName))
        {
            return null;
        }

        return windows.FirstOrDefault(w =>
            !string.IsNullOrWhiteSpace(w.DetailName) &&
            string.Equals(w.DetailName, detailName, StringComparison.OrdinalIgnoreCase) &&
            (detail.QuotaBucketKind == WindowKind.None || w.Kind == detail.QuotaBucketKind));
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
