// <copyright file="MainWindowRuntimeLogic.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
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

    public static bool GetIsCollapsedForGroup(AppPreferences preferences, string groupId)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences.CollapsedGroupIds.TryGetValue(groupId, out var collapsed) && collapsed;
    }

    public static void SetIsCollapsedForGroup(AppPreferences preferences, string groupId, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (isCollapsed)
        {
            preferences.CollapsedGroupIds[groupId] = true;
        }
        else
        {
            preferences.CollapsedGroupIds.Remove(groupId);
        }
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
        var orderedUsages = renderPreparation.ToList();
        foreach (var usage in orderedUsages)
        {
            EnrichWithPeriodDuration(usage);
        }

        return orderedUsages;
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
                var definition = ProviderMetadataCatalog.Find(id);
                if (definition == null || !definition.ShowInMainWindow || hiddenSet.Contains(id))
                {
                    return false;
                }

                // Hide Missing-state cards for StandardApiKey providers.
                // These show "API Key missing" which is not actionable in the main window.
                if (usage.State == ProviderUsageState.Missing &&
                    definition.SettingsMode == ProviderSettingsMode.StandardApiKey)
                {
                    return false;
                }

                return true;
            })
            .GroupBy(usage => $"{usage.ProviderId ?? string.Empty}::{usage.CardId ?? string.Empty}", StringComparer.OrdinalIgnoreCase)
            .Select(SelectPreferredUsage)
            .OrderByDescending(usage => usage.IsQuotaBased)
            .ToList();

        return filteredUsages;
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
        }
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

        if (usage.NextResetTime.HasValue)
        {
            score += 10;
        }

        return score;
    }

    /// <summary>
    /// Resolves reset times for the provider card, falling back to the parent's
    /// NextResetTime when no specific window reset is available.
    /// </summary>
    internal static IReadOnlyList<DateTime> ResolveResetTimes(ProviderUsage usage, bool suppressSingleResetFallback)
    {
        ArgumentNullException.ThrowIfNull(usage);

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
    /// information for multi-day quota periods.
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
            tooltipBuilder.AppendLine(CultureInfo.InvariantCulture, $"Daily budget: {dailyBudget:F0}%/day");
            tooltipBuilder.AppendLine(CultureInfo.InvariantCulture, $"Expected by now: {expectedAtThisPoint:F0}% | Actual: {usage.UsedPercent:F0}%");
        }

        if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine($"Source: {usage.AuthSource}");
        }

        var result = tooltipBuilder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
