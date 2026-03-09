// <copyright file="ProviderSubDetailPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSubDetailPresentationCatalog
{
    public static IReadOnlyList<ProviderUsageDetail> GetDisplayableDetails(ProviderUsage usage)
    {
        if (usage.Details?.Any() != true)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        return usage.Details
            .Where(IsDisplayableDetail)
            .OrderBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ProviderSubDetailPresentation Create(
        ProviderUsageDetail detail,
        bool isQuotaBased,
        bool showUsed,
        Func<DateTime, string> relativeTimeFormatter)
    {
        var parsedUsed = UsageMath.GetEffectiveUsedPercent(detail, isQuotaBased);
        var hasPercent = parsedUsed.HasValue;
        var usedPercent = parsedUsed ?? 0;
        var remainingPercent = 100.0 - usedPercent;
        var displayPercent = showUsed ? usedPercent : remainingPercent;
        var displayText = hasPercent
            ? $"{displayPercent:F0}%"
            : string.IsNullOrWhiteSpace(detail.Used) ? "Unknown" : detail.Used;
        var indicatorWidth = Math.Clamp(displayPercent, 0, 100);
        var resetText = detail.NextResetTime.HasValue
            ? $"({relativeTimeFormatter(detail.NextResetTime.Value)})"
            : null;

        return new ProviderSubDetailPresentation(
            HasProgress: hasPercent,
            UsedPercent: usedPercent,
            IndicatorWidth: indicatorWidth,
            DisplayText: displayText,
            ResetText: resetText);
    }

    private static bool IsDisplayableDetail(ProviderUsageDetail detail)
    {
        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            return false;
        }

        return detail.DetailType == ProviderUsageDetailType.Model ||
               detail.DetailType == ProviderUsageDetailType.Other;
    }
}
