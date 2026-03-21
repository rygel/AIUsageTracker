// <copyright file="TrayConfigRefreshDecisionCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class TrayConfigRefreshDecisionCatalog
{
    public static bool ShouldRefresh(
        bool hasCachedConfigs,
        DateTime lastRefreshUtc,
        DateTime nowUtc,
        TimeSpan refreshInterval)
    {
        if (!hasCachedConfigs)
        {
            return true;
        }

        return (nowUtc - lastRefreshUtc) >= refreshInterval;
    }
}
