// <copyright file="FetchDataSuccessPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class FetchDataSuccessPresentationCatalog
{
    public static FetchDataSuccessPresentation Create(
        DateTime now,
        string statusSuffix,
        bool hasPollingTimer,
        TimeSpan currentInterval,
        TimeSpan normalInterval)
    {
        return new FetchDataSuccessPresentation(
            StatusMessage: $"{now:HH:mm:ss}{statusSuffix}",
            SwitchToNormalInterval: hasPollingTimer && currentInterval != normalInterval);
    }
}
