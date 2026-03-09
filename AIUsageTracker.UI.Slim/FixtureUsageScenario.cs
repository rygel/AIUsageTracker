// <copyright file="FixtureUsageScenario.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record FixtureUsageScenario(
    double RequestsPercentage = 0,
    double RequestsUsed = 0,
    double RequestsAvailable = 0,
    string Description = "Connected",
    int? ResetHours = null);
