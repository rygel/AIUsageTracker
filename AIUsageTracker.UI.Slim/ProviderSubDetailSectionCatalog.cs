// <copyright file="ProviderSubDetailSectionCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSubDetailSectionCatalog
{
    public static ProviderSubDetailSection? Build(ProviderUsage usage, AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(preferences);

        var details = GetDisplayableDetails(usage);
        if (details.Count == 0)
        {
            return null;
        }

        var providerId = usage.ProviderId ?? string.Empty;
        var title = $"{ProviderMetadataCatalog.ResolveDisplayLabel(usage)} Details";
        var isCollapsed = GetIsCollapsed(preferences, providerId);

        return new ProviderSubDetailSection(
            ProviderId: providerId,
            Title: title,
            Details: details,
            IsCollapsed: isCollapsed);
    }

    public static IReadOnlyList<ProviderUsageDetail> GetDisplayableDetails(ProviderUsage usage)
    {
        if (usage.Details?.Any() != true)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        if (ProviderMetadataCatalog.HasDisplayableDerivedProviders(usage.ProviderId ?? string.Empty))
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        if (ProviderMetadataCatalog.IsTooltipOnlyProvider(usage.ProviderId ?? string.Empty))
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        return usage.Details
            .Where(IsDisplayableDetail)
            .OrderBy(GetDetailSortOrder)
            .ThenBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static (bool HasProgress, double UsedPercent, double IndicatorWidth, string DisplayText, string? ResetText)
        BuildDetailPresentation(
            ProviderUsageDetail detail,
            bool showUsed,
            Func<DateTime, string> relativeTimeFormatter)
    {
        var parsedUsed = UsageMath.GetEffectiveUsedPercent(detail);
        var hasPercent = parsedUsed.HasValue;
        var usedPercent = parsedUsed ?? 0;
        var remainingPercent = 100.0 - usedPercent;
        var displayPercent = showUsed ? usedPercent : remainingPercent;
        var displayText = hasPercent
            ? GetDisplayText(detail, showUsed, includeSemanticLabel: false)
            : GetStoredDisplayText(detail);
        var indicatorWidth = Math.Clamp(displayPercent, 0, 100);
        var resetText = detail.NextResetTime.HasValue
            ? $"({relativeTimeFormatter(detail.NextResetTime.Value)})"
            : null;

        return (
            HasProgress: hasPercent,
            UsedPercent: usedPercent,
            IndicatorWidth: indicatorWidth,
            DisplayText: displayText,
            ResetText: resetText);
    }

    public static bool IsEligibleDetail(ProviderUsageDetail detail, bool includeRateLimit = true)
    {
        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            return false;
        }

        return detail.DetailType == ProviderUsageDetailType.Model ||
               detail.DetailType == ProviderUsageDetailType.Other ||
               (includeRateLimit && detail.DetailType == ProviderUsageDetailType.RateLimit);
    }

    public static string GetStoredDisplayText(ProviderUsageDetail detail, bool includeComplement = false)
    {
        if (detail.TryGetPercentageValue(out var percentage, out var semantic, out var decimalPlaces))
        {
            return FormatPercentage(percentage, semantic, decimalPlaces, includeComplement);
        }

        return string.IsNullOrWhiteSpace(detail.Description) ? "No data" : detail.Description;
    }

    public static string GetDisplayText(
        ProviderUsageDetail detail,
        bool showUsed,
        bool includeSemanticLabel,
        bool includeComplement = false)
    {
        var usedPercent = UsageMath.GetEffectiveUsedPercent(detail);
        if (!usedPercent.HasValue)
        {
            return GetStoredDisplayText(detail, includeComplement: false);
        }

        var decimalPlaces = detail.TryGetPercentageValue(out _, out _, out var precision)
            ? precision
            : 0;
        var displayPercent = showUsed
            ? UsageMath.ClampPercent(usedPercent.Value)
            : UsageMath.ClampPercent(100.0 - usedPercent.Value);

        if (!includeSemanticLabel)
        {
            return $"{displayPercent.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)}%";
        }

        var semantic = showUsed ? PercentageValueSemantic.Used : PercentageValueSemantic.Remaining;
        return FormatPercentage(displayPercent, semantic, decimalPlaces, includeComplement);
    }

    public static bool GetIsCollapsed(AppPreferences preferences, string providerId)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return ShouldUseSharedCollapsePreference(providerId) && preferences.IsAntigravityCollapsed;
    }

    public static void SetIsCollapsed(AppPreferences preferences, string providerId, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!ShouldUseSharedCollapsePreference(providerId))
        {
            return;
        }

        preferences.IsAntigravityCollapsed = isCollapsed;
    }

    private static bool ShouldUseSharedCollapsePreference(string providerId)
    {
        return ProviderMetadataCatalog.ShouldUseSharedSubDetailCollapsePreference(providerId ?? string.Empty);
    }

    private static bool IsDisplayableDetail(ProviderUsageDetail detail) => IsEligibleDetail(detail, includeRateLimit: true);

    private static int GetDetailSortOrder(ProviderUsageDetail detail)
    {
        return detail.DetailType switch
        {
            ProviderUsageDetailType.Model => 0,
            ProviderUsageDetailType.RateLimit => 1,
            ProviderUsageDetailType.Other => 2,
            _ => 3,
        };
    }

    private static string FormatPercentage(
        double percentage,
        PercentageValueSemantic semantic,
        int decimalPlaces,
        bool includeComplement)
    {
        var format = $"F{Math.Max(0, decimalPlaces)}";
        var value = UsageMath.ClampPercent(percentage).ToString(format, CultureInfo.InvariantCulture);
        var semanticLabel = semantic switch
        {
            PercentageValueSemantic.Used => "used",
            PercentageValueSemantic.Remaining => "remaining",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(semanticLabel))
        {
            return $"{value}%";
        }

        if (!includeComplement)
        {
            return $"{value}% {semanticLabel}";
        }

        var complementValue = UsageMath.ClampPercent(100.0 - percentage).ToString(format, CultureInfo.InvariantCulture);
        var complementLabel = semantic == PercentageValueSemantic.Used ? "remaining" : "used";
        return $"{value}% {semanticLabel} ({complementValue}% {complementLabel})";
    }
}
