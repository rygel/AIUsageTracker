// <copyright file="MonitorActivitySources.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;

namespace AIUsageTracker.Monitor.Services;

internal static class MonitorActivitySources
{
    public const string RefreshSourceName = "AIUsageTracker.Monitor.Refresh";

    public const string SchedulerSourceName = "AIUsageTracker.Monitor.Scheduler";

    public static readonly ActivitySource Refresh = new(RefreshSourceName);

    public static readonly ActivitySource Scheduler = new(SchedulerSourceName);
}
