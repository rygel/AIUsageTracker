// <copyright file="MainWindowDeterministicFixtureData.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed class MainWindowDeterministicFixtureData
{
    public required AppPreferences Preferences { get; init; }

    public required DateTime LastMonitorUpdate { get; init; }

    public required List<ProviderUsage> Usages { get; init; }

    public double WindowWidth { get; init; } = 460;
}
