// <copyright file="MonitorControlPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class MonitorControlPresentationCatalog
{
    public static MonitorControlPresentation CreateRestarting()
    {
        return new MonitorControlPresentation(
            Message: "Restarting monitor...",
            StatusType: StatusType.Warning,
            UpdateToggleButton: false,
            ToggleRunningState: false,
            TriggerRefreshData: false);
    }

    public static MonitorControlPresentation CreateRestartResult(bool monitorReady)
    {
        return monitorReady
            ? new MonitorControlPresentation(
                Message: "Monitor restarted",
                StatusType: StatusType.Success,
                UpdateToggleButton: false,
                ToggleRunningState: false,
                TriggerRefreshData: true)
            : new MonitorControlPresentation(
                Message: "Monitor restart failed",
                StatusType: StatusType.Error,
                UpdateToggleButton: false,
                ToggleRunningState: false,
                TriggerRefreshData: false);
    }

    public static MonitorControlPresentation CreateRestartError(string errorMessage)
    {
        return new MonitorControlPresentation(
            Message: $"Restart error: {errorMessage}",
            StatusType: StatusType.Error,
            UpdateToggleButton: false,
            ToggleRunningState: false,
            TriggerRefreshData: false);
    }

    public static MonitorControlPresentation CreateStopping()
    {
        return new MonitorControlPresentation(
            Message: "Stopping monitor...",
            StatusType: StatusType.Warning,
            UpdateToggleButton: false,
            ToggleRunningState: false,
            TriggerRefreshData: false);
    }

    public static MonitorControlPresentation CreateStopResult(bool stopped)
    {
        return stopped
            ? new MonitorControlPresentation(
                Message: "Monitor stopped",
                StatusType: StatusType.Info,
                UpdateToggleButton: true,
                ToggleRunningState: false,
                TriggerRefreshData: false)
            : new MonitorControlPresentation(
                Message: "Failed to stop monitor",
                StatusType: StatusType.Error,
                UpdateToggleButton: false,
                ToggleRunningState: false,
                TriggerRefreshData: false);
    }

    public static MonitorControlPresentation CreateStarting()
    {
        return new MonitorControlPresentation(
            Message: "Starting monitor...",
            StatusType: StatusType.Warning,
            UpdateToggleButton: false,
            ToggleRunningState: false,
            TriggerRefreshData: false);
    }

    public static MonitorControlPresentation CreateStartResult(bool monitorReady)
    {
        return monitorReady
            ? new MonitorControlPresentation(
                Message: "Monitor started",
                StatusType: StatusType.Success,
                UpdateToggleButton: true,
                ToggleRunningState: true,
                TriggerRefreshData: true)
            : new MonitorControlPresentation(
                Message: "Monitor failed to start",
                StatusType: StatusType.Error,
                UpdateToggleButton: true,
                ToggleRunningState: false,
                TriggerRefreshData: false);
    }
}
