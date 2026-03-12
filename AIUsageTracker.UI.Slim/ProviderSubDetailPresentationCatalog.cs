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

        if (ProviderCapabilityCatalog.HasVisibleDerivedProviders(usage.ProviderId ?? string.Empty))
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        if (ShouldSuppressSubDetailsForTooltipOnlyProvider(usage.ProviderId))
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

    private static bool ShouldSuppressSubDetailsForTooltipOnlyProvider(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        return OpenCodeZenProvider.StaticDefinition.HandledProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetDetailSortOrder(ProviderUsageDetail detail)
    {
        return (detail.DetailType, detail.QuotaBucketKind) switch
        {
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Primary) => 0,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Secondary) => 1,
            (ProviderUsageDetailType.QuotaWindow, _) => 2,
            (ProviderUsageDetailType.Model, _) => 3,
            (ProviderUsageDetailType.Other, _) => 4,
            _ => 5,
        };
    }
}
