// <copyright file="PollingRefreshDecisionCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class PollingRefreshDecisionCatalog
{
    public static PollingRefreshDecision Create(
        DateTime lastRefreshTrigger,
        DateTime now,
        double refreshCooldownSeconds)
    {
        var secondsSinceLastRefresh = (now - lastRefreshTrigger).TotalSeconds;
        var shouldTriggerRefresh = secondsSinceLastRefresh >= refreshCooldownSeconds;
        return new PollingRefreshDecision(shouldTriggerRefresh, secondsSinceLastRefresh);
    }
}
