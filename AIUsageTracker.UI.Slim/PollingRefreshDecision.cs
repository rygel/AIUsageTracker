// <copyright file="PollingRefreshDecision.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record PollingRefreshDecision(
    bool ShouldTriggerRefresh,
    double SecondsSinceLastRefresh);
