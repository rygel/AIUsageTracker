// <copyright file="RapidPollPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class RapidPollPresentationCatalog
{
    public static RapidPollPresentation CreateInitialLoading()
    {
        return new RapidPollPresentation(
            StatusMessage: "Loading data...",
            StatusType: StatusType.Info,
            ErrorStateMessage: null);
    }

    public static RapidPollPresentation CreateMonitorNotReachable(string connectionErrorMessage)
    {
        return new RapidPollPresentation(
            StatusMessage: "Monitor not reachable",
            StatusType: StatusType.Error,
            ErrorStateMessage: connectionErrorMessage);
    }

    public static RapidPollPresentation CreateScanningForProviders()
    {
        return new RapidPollPresentation(
            StatusMessage: "Scanning for providers...",
            StatusType: StatusType.Info,
            ErrorStateMessage: null);
    }

    public static RapidPollPresentation CreateWaitingForData(int attempt, int maxAttempts)
    {
        return new RapidPollPresentation(
            StatusMessage: $"Waiting for data... ({attempt + 1}/{maxAttempts})",
            StatusType: StatusType.Warning,
            ErrorStateMessage: null);
    }

    public static RapidPollPresentation CreateConnectionLost(string exceptionMessage)
    {
        return new RapidPollPresentation(
            StatusMessage: "Connection lost",
            StatusType: StatusType.Error,
            ErrorStateMessage: $"Lost connection to Monitor:\n{exceptionMessage}\n\nTry refreshing or restarting the Monitor.");
    }

    public static RapidPollPresentation CreateNoDataAfterMaxAttempts()
    {
        return new RapidPollPresentation(
            StatusMessage: "No data available",
            StatusType: StatusType.Error,
            ErrorStateMessage: "No provider data available.\n\nThe Monitor may still be initializing.\nTry refreshing manually or check Settings > Monitor.");
    }

    public static bool ShouldTriggerBackgroundRefresh(int attempt)
    {
        return attempt == 0;
    }

    public static bool ShouldWaitBeforeRetry(int attempt, int maxAttempts)
    {
        return attempt < maxAttempts - 1;
    }
}
