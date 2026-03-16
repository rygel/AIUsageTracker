// <copyright file="ProviderSettingsBehavior.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderSettingsBehavior(
    ProviderInputMode InputMode,
    bool IsInactive,
    bool IsDerivedVisible,
    string? SessionProviderLabel);
