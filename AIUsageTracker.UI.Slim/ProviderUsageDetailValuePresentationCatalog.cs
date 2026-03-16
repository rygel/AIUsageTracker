// <copyright file="ProviderUsageDetailValuePresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderUsageDetailValuePresentationCatalog
{
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
