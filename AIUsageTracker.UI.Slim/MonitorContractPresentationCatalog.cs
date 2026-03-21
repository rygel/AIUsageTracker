// <copyright file="MonitorContractPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class MonitorContractPresentationCatalog
{
    public static MonitorContractPresentation Create(bool isCompatible, string message)
    {
        if (isCompatible)
        {
            return new MonitorContractPresentation(
                WarningMessage: null,
                ShowStatus: false,
                StatusMessage: null,
                StatusType: null);
        }

        return new MonitorContractPresentation(
            WarningMessage: message,
            ShowStatus: true,
            StatusMessage: message,
            StatusType: StatusType.Warning);
    }
}
