// <copyright file="IMonitorJobScheduler.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

public interface IMonitorJobScheduler
{
    bool Enqueue(
        string jobName,
        Func<CancellationToken, Task> work,
        MonitorJobPriority priority = MonitorJobPriority.Normal,
        string? coalesceKey = null);

    void RegisterRecurringJob(
        string jobName,
        TimeSpan interval,
        Func<CancellationToken, Task> work,
        MonitorJobPriority priority = MonitorJobPriority.Normal,
        TimeSpan? initialDelay = null,
        string? coalesceKey = null);

    void Pause();

    void Resume();

    MonitorJobSchedulerSnapshot GetSnapshot();
}
