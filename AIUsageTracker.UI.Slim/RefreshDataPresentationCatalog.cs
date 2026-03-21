// <copyright file="RefreshDataPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class RefreshDataPresentationCatalog
{
    public static RefreshDataPresentation Create(
        bool hasLatestUsages,
        bool hasCurrentUsages,
        DateTime now)
    {
        if (hasLatestUsages)
        {
            return new RefreshDataPresentation(
                ApplyLatestUsages: true,
                UpdateLastMonitorTimestamp: true,
                StatusMessage: $"{now:HH:mm:ss}",
                StatusType: StatusType.Success,
                TriggerTrayIconUpdate: true,
                UseErrorState: false,
                ErrorStateMessage: null);
        }

        if (hasCurrentUsages)
        {
            return new RefreshDataPresentation(
                ApplyLatestUsages: false,
                UpdateLastMonitorTimestamp: false,
                StatusMessage: "Refresh returned no data, keeping last snapshot",
                StatusType: StatusType.Warning,
                TriggerTrayIconUpdate: false,
                UseErrorState: false,
                ErrorStateMessage: null);
        }

        return new RefreshDataPresentation(
            ApplyLatestUsages: false,
            UpdateLastMonitorTimestamp: false,
            StatusMessage: null,
            StatusType: null,
            TriggerTrayIconUpdate: false,
            UseErrorState: true,
            ErrorStateMessage: "No provider data available.\n\nMonitor may still be initializing.");
    }
}
