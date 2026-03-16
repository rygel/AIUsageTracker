// <copyright file="DeterministicProviderScenario.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record DeterministicProviderScenario(
    string ProviderId,
    string ApiKey,
    bool ShowInTray = false,
    bool EnableNotifications = false,
    string AuthSource = "api key",
    FixtureUsageScenario? MainWindowUsage = null,
    FixtureUsageScenario? SettingsWindowUsage = null,
    FixtureHistoryScenario? SettingsWindowHistory = null);
