// <copyright file="ProviderRefreshJobScheduler.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderRefreshJobScheduler
{
    private const string ManualRefreshJobName = "manual-provider-refresh";
    private const string ScheduledRefreshJobName = "scheduled-provider-refresh";
    private const string StartupSeedingJobName = "startup-provider-seeding";
    private const string StartupTargetedRefreshJobName = "startup-targeted-provider-refresh";
    private const string ManualRefreshCoalesceKey = "manual-provider-refresh";
    private const string ScheduledRefreshCoalesceKey = "scheduled-provider-refresh";
    private const string StartupSeedingCoalesceKey = "startup-provider-seeding";
    private const string StartupTargetedRefreshCoalesceKey = "startup-targeted-provider-refresh";

    private readonly IMonitorJobScheduler _jobScheduler;
    private readonly ILogger<ProviderRefreshJobScheduler> _logger;

    public ProviderRefreshJobScheduler(
        IMonitorJobScheduler jobScheduler,
        ILogger<ProviderRefreshJobScheduler> logger)
    {
        this._jobScheduler = jobScheduler;
        this._logger = logger;
    }

    public void RegisterRecurringRefresh(
        TimeSpan interval,
        Func<CancellationToken, Task> refreshTask)
    {
        this._jobScheduler.RegisterRecurringJob(
            ScheduledRefreshJobName,
            interval,
            refreshTask,
            MonitorJobPriority.Low,
            initialDelay: interval,
            coalesceKey: ScheduledRefreshCoalesceKey);
    }

    public bool QueueManualRefresh(
        Func<CancellationToken, Task> refreshTask,
        string? coalesceKey = null)
    {
        var effectiveCoalesceKey = string.IsNullOrWhiteSpace(coalesceKey)
            ? ManualRefreshCoalesceKey
            : coalesceKey;
        var queued = this._jobScheduler.Enqueue(
            ManualRefreshJobName,
            refreshTask,
            MonitorJobPriority.High,
            coalesceKey: effectiveCoalesceKey);

        if (!queued)
        {
            this._logger.LogDebug("Manual refresh job was already queued.");
        }

        return queued;
    }

    public bool QueueInitialDataSeeding(Func<CancellationToken, Task> seedingTask)
    {
        var queued = this._jobScheduler.Enqueue(
            StartupSeedingJobName,
            seedingTask,
            MonitorJobPriority.High,
            coalesceKey: StartupSeedingCoalesceKey);

        if (!queued)
        {
            this._logger.LogDebug("Startup seeding job was already queued.");
        }

        return queued;
    }

    public bool QueueStartupTargetedRefresh(Func<CancellationToken, Task> refreshTask)
    {
        var queued = this._jobScheduler.Enqueue(
            StartupTargetedRefreshJobName,
            refreshTask,
            MonitorJobPriority.Low,
            coalesceKey: StartupTargetedRefreshCoalesceKey);

        if (!queued)
        {
            this._logger.LogDebug("Startup targeted refresh job was already queued.");
        }

        return queued;
    }
}
