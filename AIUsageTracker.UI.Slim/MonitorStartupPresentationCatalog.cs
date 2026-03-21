// <copyright file="MonitorStartupPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class MonitorStartupPresentationCatalog
{
    public static bool ShouldShowConnectionFailureState(bool hasUsages, int providersListChildCount)
    {
        return !hasUsages && providersListChildCount <= 1;
    }
}
