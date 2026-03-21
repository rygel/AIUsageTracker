// <copyright file="PollingPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class PollingPresentationCatalog
{
    public static TimeSpan ResolveInitialInterval(
        bool hasUsages,
        TimeSpan startupInterval,
        TimeSpan normalInterval)
    {
        return hasUsages ? normalInterval : startupInterval;
    }

    public static PollingStatusPresentation ResolveAfterEmptyRetry(
        bool hasCurrentUsages,
        DateTime lastMonitorUpdate,
        DateTime now)
    {
        if (!hasCurrentUsages)
        {
            return new PollingStatusPresentation(
                Message: "No data - waiting for Monitor",
                StatusType: StatusType.Warning,
                SwitchToStartupInterval: true);
        }

        if ((now - lastMonitorUpdate).TotalMinutes > 5)
        {
            return new PollingStatusPresentation(
                Message: MonitorOfflineStatusCatalog.Format(lastMonitorUpdate, now),
                StatusType: StatusType.Warning,
                SwitchToStartupInterval: false);
        }

        return new PollingStatusPresentation(
            Message: null,
            StatusType: null,
            SwitchToStartupInterval: false);
    }

    public static PollingStatusPresentation ResolveOnPollingException(
        bool hasOldData,
        DateTime lastMonitorUpdate,
        DateTime now)
    {
        if (hasOldData)
        {
            return new PollingStatusPresentation(
                Message: MonitorOfflineStatusCatalog.Format(lastMonitorUpdate, now),
                StatusType: StatusType.Warning,
                SwitchToStartupInterval: false);
        }

        return new PollingStatusPresentation(
            Message: "Connection error",
            StatusType: StatusType.Error,
            SwitchToStartupInterval: true);
    }
}
