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

        // aggDef is already resolved above — use it directly instead of a second catalog lookup.
        var planType = aggDef.PlanType;
        var isQuotaBased = aggDef.IsQuotaBased;
        var quotaWindows = aggDef.QuotaWindows;
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
            score += 5;
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

    /// <summary>
    /// Resolves reset times from detail-level quota windows, falling back to the parent's
    /// NextResetTime when fewer than two distinct detail reset times are available.
    /// </summary>
    internal static IReadOnlyList<DateTime> ResolveResetTimes(ProviderUsage usage, bool suppressSingleResetFallback)
    {
        ArgumentNullException.ThrowIfNull(usage);

        // Only show Burst and Rolling reset times on the parent card.
        // ModelSpecific windows (e.g. Spark) have their own child card.
        var detailResetTimes = usage.Details?
            .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
            .Where(detail => detail.QuotaBucketKind is WindowKind.Burst or WindowKind.Rolling)
            .Where(detail => detail.NextResetTime.HasValue)
            .Where(detail => UsageMath.GetEffectiveUsedPercent(detail).HasValue)
            .Select(detail => detail.NextResetTime!.Value)
            .Distinct()
            .ToList()
            ?? new List<DateTime>();

        if (detailResetTimes.Count >= 2)
        {
            return detailResetTimes;
        }

        if (suppressSingleResetFallback)
        {
            return Array.Empty<DateTime>();
        }

        return usage.NextResetTime.HasValue
            ? new[] { usage.NextResetTime.Value }
            : Array.Empty<DateTime>();
    }

    /// <summary>
    /// Builds a multi-line tooltip string for a provider card, including daily budget
    /// information for multi-day quota periods and per-detail rate limit breakdowns.
    /// </summary>
    internal static string? BuildTooltipContent(ProviderUsage usage, string friendlyName)
    {
        var tooltipBuilder = new System.Text.StringBuilder();
        tooltipBuilder.AppendLine(friendlyName);
        tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
        if (!string.IsNullOrEmpty(usage.Description))
        {
            tooltipBuilder.AppendLine($"Description: {usage.Description}");
        }

        if (usage.IsAvailable && usage.PeriodDuration.HasValue && usage.PeriodDuration.Value.TotalDays >= 1)
        {
            var dailyBudget = 100.0 / usage.PeriodDuration.Value.TotalDays;
            var elapsedDays = UsageMath.GetElapsedDays(usage.NextResetTime, usage.PeriodDuration);
            var expectedAtThisPoint = dailyBudget * elapsedDays;
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine($"Daily budget: {dailyBudget:F0}%/day");
            tooltipBuilder.AppendLine($"Expected by now: {expectedAtThisPoint:F0}% | Actual: {usage.UsedPercent:F0}%");
        }

        if (usage.Details?.Any() == true)
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine("Rate Limits:");
            foreach (var detail in usage.Details
                         .OrderBy(GetTooltipDetailSortOrder)
                         .ThenBy(GetDetailDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var detailValue = GetDetailDisplayValue(detail);
                if (string.IsNullOrWhiteSpace(detailValue))
                {
                    continue;
                }

                tooltipBuilder.AppendLine($"  {GetDetailDisplayName(detail)}: {detailValue}");
            }
        }

        if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine($"Source: {usage.AuthSource}");
        }

        var result = tooltipBuilder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    /// <summary>
    /// Gets the display name for a detail row (used in tooltip rendering).
    /// </summary>
    internal static string GetDetailDisplayName(ProviderUsageDetail detail)
    {
        return detail.Name;
    }

    /// <summary>
    /// Gets the formatted display value for a detail row (used in tooltip rendering).
    /// </summary>
    internal static string GetDetailDisplayValue(ProviderUsageDetail detail)
    {
        return GetStoredDisplayText(detail);
    }

    /// <summary>
    /// Sort order for tooltip detail rows, grouping quota windows by bucket kind
    /// before model and credit details.
    /// </summary>
    private static int GetTooltipDetailSortOrder(ProviderUsageDetail detail)
    {
        return (detail.DetailType, detail.QuotaBucketKind) switch
        {
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Burst) => 0,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Rolling) => 1,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.ModelSpecific) => 2,
            (ProviderUsageDetailType.QuotaWindow, _) => 3,
            (ProviderUsageDetailType.Model, _) => 3,
            (ProviderUsageDetailType.Credit, _) => 4,
            _ => 5,
        };
    }
}
