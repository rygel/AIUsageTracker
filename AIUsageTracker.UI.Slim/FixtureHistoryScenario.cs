// <copyright file="FixtureHistoryScenario.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record FixtureHistoryScenario(
    double UsagePercentage,
    double Used,
    double Limit,
    string Description,
    DateTime FetchedAt);
