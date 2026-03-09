// <copyright file="ProviderSubDetailPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderSubDetailPresentation(
    bool HasProgress,
    double UsedPercent,
    double IndicatorWidth,
    string DisplayText,
    string? ResetText);
