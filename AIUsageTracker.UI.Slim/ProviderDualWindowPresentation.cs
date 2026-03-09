// <copyright file="ProviderDualWindowPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderDualWindowPresentation(
    string PrimaryLabel,
    double PrimaryUsedPercent,
    DateTime? PrimaryResetTime,
    string SecondaryLabel,
    double SecondaryUsedPercent,
    DateTime? SecondaryResetTime);
