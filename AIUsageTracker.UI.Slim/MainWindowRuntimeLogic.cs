// <copyright file="MainWindowRuntimeLogic.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Core.Providers;

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
        string ago;
        if (elapsed.TotalSeconds < 60)
        {
            ago = $"{(int)elapsed.TotalSeconds}s ago";
        }
        else if (elapsed.TotalHours < 1)
        {
            ago = $"{((int)elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture)}m ago";
        }
        else
        {
            ago = $"{((int)elapsed.TotalHours).ToString(CultureInfo.InvariantCulture)}h ago";
        }

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

                return true;
            })
            .GroupBy(usage => $"{usage.ProviderId ?? string.Empty}::{(usage is WindowedProviderUsage w ? w.CardId : (usage as ModelScopedProviderUsage)?.CardId) ?? string.Empty}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(usage => usage.FetchedAt).First())
            .OrderByDescending(usage => usage is QuotaProviderUsage q && q.IsQuotaBased)
            .ToList();

        return filteredUsages;
    }

    public static IReadOnlyList<ProviderSectionLayout> BuildProviderSectionLayouts(IReadOnlyList<ProviderUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(usages);
        if (usages.Count == 0)
        {
            throw new ArgumentException("Usages collection must not be empty — caller guarantees non-empty before calling.", nameof(usages));
        }

        var sections = new List<ProviderSectionLayout>();
        bool? currentIsQuota = null;
        List<ProviderUsage>? currentItems = null;

        foreach (var usage in usages)
        {
            var isQuotaBased = usage is QuotaProviderUsage q && q.IsQuotaBased;
            if (currentIsQuota != isQuotaBased || currentItems == null)
            {
                currentIsQuota = isQuotaBased;
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
        var existing = usage switch
        {
            WindowedProviderUsage w => w.PeriodDuration,
            ModelScopedProviderUsage m => m.PeriodDuration,
            _ => null,
        };
        if (existing.HasValue)
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
            switch (usage)
            {
                case WindowedProviderUsage w:
                    w.PeriodDuration = duration;
                    break;
                case ModelScopedProviderUsage m:
                    m.PeriodDuration = duration;
                    break;
            }
        }
    }

    internal static IReadOnlyList<DateTime> ResolveResetTimes(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (usage is QuotaProviderUsage q && q.NextResetTime.HasValue)
        {
            return new[] { q.NextResetTime.Value };
        }

        return Array.Empty<DateTime>();
    }

    internal static string? ResolveResetWindowLabel(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var providerId = usage.ProviderId ?? string.Empty;
        var ownerProviderId = ProviderMetadataCatalog.GetProviderOwnerId(providerId);
        if (string.Equals(
            ownerProviderId,
            GitHubCopilotProvider.StaticDefinition.ProviderId,
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var cardName = usage is WindowedProviderUsage w ? w.Name : (usage as ModelScopedProviderUsage)?.Name;
        var cardId = usage is WindowedProviderUsage w2 ? w2.CardId : (usage as ModelScopedProviderUsage)?.CardId;
        var explicitLabel = NormalizeResetWindowLabel(cardName)
            ?? NormalizeResetWindowLabel(cardId)
            ?? NormalizeResetWindowLabel(usage.ProviderName);
        if (!string.IsNullOrWhiteSpace(explicitLabel))
        {
            return explicitLabel;
        }

        if (ProviderMetadataCatalog.TryGet(ownerProviderId, out var definition))
        {
            var matchedWindow = definition.QuotaWindows.FirstOrDefault(window =>
                !string.IsNullOrWhiteSpace(window.ChildProviderId) &&
                string.Equals(window.ChildProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            var matchedLabel = NormalizeResetWindowLabel(matchedWindow?.DualBarLabel)
                ?? NormalizeResetWindowLabel(matchedWindow?.SettingsLabel)
                ?? NormalizeResetWindowLabel(
                    definition.QuotaWindows
                        .FirstOrDefault(window => window.Kind == WindowKind.Burst)
                        ?.DualBarLabel);
            if (!string.IsNullOrWhiteSpace(matchedLabel))
            {
                return matchedLabel;
            }
        }

        return usage is QuotaProviderUsage q && q.PlanType == PlanType.Coding && q.IsQuotaBased
            ? "5h"
            : null;
    }

    internal static (DateTime? ResetTime, string? ResetLabel) ResolveCardResetDisplay(
        ProviderUsage usage,
        ProviderCardPresentation presentation,
        bool showDualQuotaBars,
        DualQuotaSingleBarMode dualQuotaSingleBarMode)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(presentation);

        if (presentation.DualBar != null)
        {
            var bar = showDualQuotaBars
                ? presentation.DualBar.Primary // Dual-bar layout always surfaces the burst (5h) reset.
                : dualQuotaSingleBarMode == DualQuotaSingleBarMode.Burst
                    ? presentation.DualBar.Primary
                    : presentation.DualBar.Secondary;

            var label = string.IsNullOrWhiteSpace(bar.Label)
                ? ResolveResetWindowLabel(usage)
                : bar.Label;
            return (bar.ResetTime, label);
        }

        return presentation.SuppressSingleResetTime
            ? (null, null)
            : (usage is QuotaProviderUsage q ? q.NextResetTime : null, ResolveResetWindowLabel(usage));
    }

    /// <summary>
    /// Builds a multi-line tooltip string for a provider card, including daily budget
    /// information for multi-day quota periods.
    /// </summary>
    /// <returns></returns>
    internal static string? BuildTooltipContent(ProviderUsage usage, string friendlyName, bool useRelativeResetTime = false, bool showUsed = false)
    {
        var tooltipBuilder = new System.Text.StringBuilder();
        tooltipBuilder.AppendLine(friendlyName);
        var parentPid = usage is WindowedProviderUsage w ? w.ParentProviderId : null;
        var modelProvider = !string.IsNullOrWhiteSpace(parentPid)
            ? parentPid
            : usage.ProviderId;
        if (!string.IsNullOrWhiteSpace(modelProvider))
        {
            tooltipBuilder.AppendLine($"Model provider: {modelProvider}");
        }

        if (usage is ModelScopedProviderUsage m && !string.IsNullOrWhiteSpace(m.ModelName))
        {
            tooltipBuilder.AppendLine($"Model: {m.ModelName}");
        }

        tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
        if (!string.IsNullOrEmpty(usage.Description))
        {
            if (usage is QuotaProviderUsage q && q.RequestsAvailable > 0)
            {
                tooltipBuilder.AppendLine(showUsed
                    ? UsageMath.FormatUsedPercent(q.UsedPercent)
                    : UsageMath.FormatRemainingPercent(q.RemainingPercent));
            }
            else
            {
                tooltipBuilder.AppendLine($"Description: {usage.Description}");
            }
        }

        if (usage is QuotaProviderUsage qReset && qReset.ResetCreditsAvailable.HasValue)
        {
            tooltipBuilder.AppendLine($"Reset credits available: {qReset.ResetCreditsAvailable.Value}");
        }

        if (ShouldRenderDerivedUsageDetails(usage))
        {
            AppendWindowLimitLines(tooltipBuilder, usage, useRelativeResetTime);
            AppendSingleResetLine(tooltipBuilder, usage, useRelativeResetTime);

            var periodDuration = usage switch
            {
                WindowedProviderUsage pw => pw.PeriodDuration,
                ModelScopedProviderUsage pm => pm.PeriodDuration,
                _ => null,
            };
            var nextReset = usage is QuotaProviderUsage q ? q.NextResetTime : null;
            var usedPct = usage is QuotaProviderUsage q2 ? q2.UsedPercent : 0;
            if (periodDuration.HasValue && periodDuration.Value.TotalDays >= 1)
            {
                var dailyBudget = 100.0 / periodDuration.Value.TotalDays;
                var elapsedDays = UsageMath.GetElapsedDays(nextReset, periodDuration);
                var expectedAtThisPoint = dailyBudget * elapsedDays;
                tooltipBuilder.AppendLine();
                tooltipBuilder.AppendLine(CultureInfo.InvariantCulture, $"Daily budget: {dailyBudget:F0}%/day");
                tooltipBuilder.AppendLine(CultureInfo.InvariantCulture, $"Expected by now: {expectedAtThisPoint:F0}% | Actual: {usedPct:F0}%");
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

    private static bool ShouldRenderDerivedUsageDetails(ProviderUsage usage)
    {
        return usage.IsAvailable && usage.State == ProviderUsageState.Available;
    }

    private static void AppendWindowLimitLines(
        System.Text.StringBuilder tooltipBuilder,
        ProviderUsage usage,
        bool useRelativeResetTime)
    {
        if (usage is not WindowedProviderUsage wu || wu.WindowCards == null || wu.WindowCards.Count == 0)
        {
            return;
        }

        var burstCard = wu.WindowCards.FirstOrDefault(card => card.WindowKind == WindowKind.Burst);
        var rollingCard = wu.WindowCards.FirstOrDefault(card => card.WindowKind == WindowKind.Rolling);
        if (burstCard == null && rollingCard == null)
        {
            return;
        }

        tooltipBuilder.AppendLine();
        AppendWindowLine(tooltipBuilder, usage, burstCard, useRelativeResetTime);
        AppendWindowLine(tooltipBuilder, usage, rollingCard, useRelativeResetTime);
    }

    private static void AppendSingleResetLine(
        System.Text.StringBuilder tooltipBuilder,
        ProviderUsage usage,
        bool useRelativeResetTime)
    {
        var qUsage = usage as QuotaProviderUsage;
        if ((usage is WindowedProviderUsage wu && (wu.WindowCards?.Count ?? 0) > 0) || qUsage?.NextResetTime == null)
        {
            return;
        }

        var resetText = FormatTooltipResetText(usage, qUsage.NextResetTime.Value, useRelativeResetTime);
        var label = ResolveResetWindowLabel(usage);
        tooltipBuilder.AppendLine();
        tooltipBuilder.AppendLine(
            string.IsNullOrWhiteSpace(label)
                ? $"Resets: {resetText}"
                : $"{label} resets: {resetText}");
    }

    private static void AppendWindowLine(
        System.Text.StringBuilder tooltipBuilder,
        ProviderUsage ownerUsage,
        ProviderUsage? windowCard,
        bool useRelativeResetTime)
    {
        if (windowCard is not WindowedProviderUsage wc || string.IsNullOrWhiteSpace(wc.Name))
        {
            return;
        }

        var label = wc.Name;
        var remainingPercent = UsageMath.ClampPercent(100.0 - wc.UsedPercent);

        tooltipBuilder.AppendLine(CultureInfo.InvariantCulture, $"{label} limit: {remainingPercent:F0}% remaining");
        if (wc.NextResetTime.HasValue)
        {
            var resetText = FormatTooltipResetText(wc, wc.NextResetTime.Value, useRelativeResetTime, ownerUsage);
            tooltipBuilder.AppendLine($"{label} resets: {resetText}");
        }
    }

    private static string FormatTooltipResetText(ProviderUsage usage, DateTime resetTime, bool useRelativeResetTime, ProviderUsage? ownerUsage = null)
    {
        if (useRelativeResetTime)
        {
            return UsageMath.FormatRelativeTime(resetTime);
        }

        // MiniMax coding-plan responses expose window times in UTC milliseconds.
        // Show explicit UTC in tooltip to avoid local-time ambiguity for this provider.
        if (IsMinimaxCodingPlanUsage(usage) || (ownerUsage != null && IsMinimaxCodingPlanUsage(ownerUsage)))
        {
            return FormatUtcResetDateTime(resetTime);
        }

        return UsageMath.FormatAbsoluteDate(resetTime);
    }

    internal static bool IsMinimaxCodingPlanUsage(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (string.Equals(usage.ProviderId, MinimaxProvider.CodingPlanProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(usage.ProviderName, "Minimax.io Coding Plan", StringComparison.OrdinalIgnoreCase);
    }

    internal static string FormatUtcResetDateTime(DateTime resetTime)
    {
        return $"{UsageMath.AsUtc(resetTime).ToString("MMM d, HH:mm", CultureInfo.InvariantCulture)} UTC";
    }

    private static string? NormalizeResetWindowLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Contains("month", StringComparison.OrdinalIgnoreCase))
        {
            return "Monthly";
        }

        if (normalized.Contains("5h", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("5-hour", StringComparison.OrdinalIgnoreCase))
        {
            return "5h";
        }

        if (normalized.Contains("week", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("7 day", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("7d", StringComparison.OrdinalIgnoreCase))
        {
            return "Weekly";
        }

        if (normalized.Contains("day", StringComparison.OrdinalIgnoreCase))
        {
            return "Daily";
        }

        if (normalized.Contains("hour", StringComparison.OrdinalIgnoreCase))
        {
            return "Hourly";
        }

        return null;
    }
}
