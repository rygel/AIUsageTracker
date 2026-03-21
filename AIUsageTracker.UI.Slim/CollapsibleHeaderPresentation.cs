// <copyright file="CollapsibleHeaderPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Media;

namespace AIUsageTracker.UI.Slim;

internal sealed record CollapsibleHeaderPresentation(
    Thickness Margin,
    double ToggleFontSize,
    FontWeight TitleFontWeight,
    double ToggleOpacity,
    double LineOpacity,
    string TitleText,
    bool UseAccentForTitle);
