// <copyright file="ProviderSubDetailPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSubDetailPresentationCatalog
{
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
            ? ProviderUsageDetailValuePresentationCatalog.GetDisplayText(
                detail,
                isQuotaBased,
                showUsed,
                includeSemanticLabel: false)
            : ProviderUsageDetailValuePresentationCatalog.GetStoredDisplayText(detail);
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
               detail.DetailType == ProviderUsageDetailType.Other ||
               detail.DetailType == ProviderUsageDetailType.RateLimit;
    }

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
}
