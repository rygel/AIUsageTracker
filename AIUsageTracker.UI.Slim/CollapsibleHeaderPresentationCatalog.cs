// <copyright file="CollapsibleHeaderPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows;

namespace AIUsageTracker.UI.Slim;

internal static class CollapsibleHeaderPresentationCatalog
{
    public static CollapsibleHeaderPresentation Create(string title, bool isGroupHeader)
    {
        var margin = isGroupHeader
            ? new Thickness(0, 8, 0, 4)
            : new Thickness(20, 4, 0, 2);
        var toggleFontSize = isGroupHeader ? 10.0 : 9.0;
        var titleFontWeight = isGroupHeader ? FontWeights.Bold : FontWeights.Normal;
        var toggleOpacity = isGroupHeader ? 1.0 : 0.8;
        var lineOpacity = isGroupHeader ? 0.5 : 0.3;
        var titleText = isGroupHeader
            ? title.ToUpper(CultureInfo.InvariantCulture)
            : title;

        return new CollapsibleHeaderPresentation(
            Margin: margin,
            ToggleFontSize: toggleFontSize,
            TitleFontWeight: titleFontWeight,
            ToggleOpacity: toggleOpacity,
            LineOpacity: lineOpacity,
            TitleText: titleText,
            UseAccentForTitle: isGroupHeader);
    }
}
