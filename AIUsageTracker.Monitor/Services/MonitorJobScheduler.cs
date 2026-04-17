// <copyright file="MonitorJobScheduler.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Diagnostics;

namespace AIUsageTracker.Monitor.Services;

public sealed class MonitorJobScheduler : BackgroundService, IMonitorJobScheduler
{
    private static readonly MonitorJobPriority[] DispatchPattern =
    [
        MonitorJobPriority.High,
        MonitorJobPriority.High,
        MonitorJobPriority.High,
        MonitorJobPriority.Normal,
        MonitorJobPriority.Normal,
        MonitorJobPriority.Low,
    ];

    private readonly ILogger<MonitorJobScheduler> _logger;
    private readonly ConcurrentQueue<ScheduledJob> _highPriorityQueue = new();
    private readonly ConcurrentQueue<ScheduledJob> _normalPriorityQueue = new();
    private readonly ConcurrentQueue<ScheduledJob> _lowPriorityQueue = new();
    private readonly ConcurrentDictionary<string, byte> _coalescedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _queuedItemsSignal = new(0);
    private readonly SemaphoreSlim _pauseGate = new(1, 1);
    private volatile bool _paused;
    private readonly object _recurringLock = new();
    private readonly List<RecurringJobRegistration> _recurringRegistrations = new();
    private readonly List<Task> _recurringTasks = new();
    private long _executedJobs;
    private long _failedJobs;
    private long _enqueuedJobs;
    private long _dequeuedJobs;
    private long _coalescedSkippedJobs;
    private long _coalescedCompletedJobs;
    private long _dispatchNoopSignals;
    private long _inFlightJobs;
    private long _maxObservedQueueWaitMs;
    private long _lastExecutionDurationMs;
    private long _totalExecutionDurationMs;
    private long _completedExecutionSamples;
    private bool _isRunning;
    private int _dispatchPatternIndex;
    private MonitorJobPriority? _lastDequeuedPriority;
    private CancellationToken _schedulerToken = CancellationToken.None;

    public MonitorJobScheduler(ILogger<MonitorJobScheduler> logger)
    {
        this._logger = logger;
    }

    public bool Enqueue(
        string jobName,
        Func<CancellationToken, Task> work,
        MonitorJobPriority priority = MonitorJobPriority.Normal,
        string? coalesceKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(work);

        if (!string.IsNullOrWhiteSpace(coalesceKey) && !this._coalescedKeys.TryAdd(coalesceKey, 0))
        {
            Interlocked.Increment(ref this._coalescedSkippedJobs);
            this._logger.LogDebug("Skipped enqueue for coalesced job {JobName} ({CoalesceKey})", jobName, coalesceKey);
            return false;
        }

        var job = new ScheduledJob(jobName, priority, work, coalesceKey, DateTime.UtcNow);
        switch (priority)
        {
            case MonitorJobPriority.High:
                this._highPriorityQueue.Enqueue(job);
                break;
            case MonitorJobPriority.Low:
                this._lowPriorityQueue.Enqueue(job);
                break;
            default:
                this._normalPriorityQueue.Enqueue(job);
                break;
        }

        this._queuedItemsSignal.Release();
        Interlocked.Increment(ref this._enqueuedJobs);
        this._logger.LogDebug("Queued job {JobName} with priority {Priority}", jobName, priority);
        return true;
    }

    public void Pause()
    {
        if (this._paused)
        {
            return;
        }

        this._paused = true;
        this._pauseGate.Wait(); // architecture-allow-sync-wait: called from synchronous SystemEvents.PowerModeChanged handler
        this._logger.LogInformation("Monitor job scheduler paused");
    }

    public void Resume()
    {
        if (!this._paused)
        {
            return;
        }

        this._paused = false;
        this._pauseGate.Release();
        this._logger.LogInformation("Monitor job scheduler resumed");
    }

    public void RegisterRecurringJob(
        string jobName,
        TimeSpan interval,
        Func<CancellationToken, Task> work,
        MonitorJobPriority priority = MonitorJobPriority.Normal,
        TimeSpan? initialDelay = null,
        string? coalesceKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(work);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Recurring interval must be greater than zero.");
        }

        var registration = new RecurringJobRegistration(
            jobName,
            interval,
            initialDelay ?? TimeSpan.Zero,
            priority,
            work,
            coalesceKey);

        lock (this._recurringLock)
        {
            this._recurringRegistrations.Add(registration);
            if (this._isRunning)
            {
                this._recurringTasks.Add(this.StartRecurringLoopAsync(registration, this._schedulerToken));
            }
        }

        this._logger.LogInformation(
            "Registered recurring job {JobName} with interval {Interval} and priority {Priority}",
            jobName,
            interval,
            priority);
    }

    public MonitorJobSchedulerSnapshot GetSnapshot()
    {
        var now = DateTime.UtcNow;
        var high = this._highPriorityQueue.Count;
        var normal = this._normalPriorityQueue.Count;
        var low = this._lowPriorityQueue.Count;
        var completedExecutionSamples = Interlocked.Read(ref this._completedExecutionSamples);
        int recurringCount;

        lock (this._recurringLock)
        {
            recurringCount = this._recurringRegistrations.Count;
        }

        return new MonitorJobSchedulerSnapshot
        {
            IsPaused = this._paused,
            HighPriorityQueuedJobs = high,
            NormalPriorityQueuedJobs = normal,
            LowPriorityQueuedJobs = low,
            TotalQueuedJobs = high + normal + low,
            RecurringJobs = recurringCount,
            ExecutedJobs = Interlocked.Read(ref this._executedJobs),
            FailedJobs = Interlocked.Read(ref this._failedJobs),
            EnqueuedJobs = Interlocked.Read(ref this._enqueuedJobs),
            DequeuedJobs = Interlocked.Read(ref this._dequeuedJobs),
            CoalescedSkippedJobs = Interlocked.Read(ref this._coalescedSkippedJobs),
            CoalescedCompletedJobs = Interlocked.Read(ref this._coalescedCompletedJobs),
            DispatchNoopSignals = Interlocked.Read(ref this._dispatchNoopSignals),
            InFlightJobs = Interlocked.Read(ref this._inFlightJobs),
            OldestQueuedJobAgeMs = this.GetOldestQueuedJobAgeMs(now),
            MaxObservedQueueWaitMs = Interlocked.Read(ref this._maxObservedQueueWaitMs),
            AverageExecutionDurationMs = completedExecutionSamples == 0
                ? 0
                : Interlocked.Read(ref this._totalExecutionDurationMs) / completedExecutionSamples,
            LastExecutionDurationMs = Interlocked.Read(ref this._lastExecutionDurationMs),
            LastDequeuedPriority = this._lastDequeuedPriority?.ToString(),
            NextDispatchPriority = DispatchPattern[this._dispatchPatternIndex].ToString(),
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this._logger.LogInformation("Monitor job scheduler starting");
        this._schedulerToken = stoppingToken;

        lock (this._recurringLock)
        {
            this._isRunning = true;
            foreach (var registration in this._recurringRegistrations)
            {
                this._recurringTasks.Add(this.StartRecurringLoopAsync(registration, stoppingToken));
            }
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await this._queuedItemsSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                await this._pauseGate.WaitAsync(stoppingToken).ConfigureAwait(false);
                this._pauseGate.Release();
                if (!this.TryDequeueNext(out var job))
                {
                    Interlocked.Increment(ref this._dispatchNoopSignals);
                    continue;
                }

                Interlocked.Increment(ref this._dequeuedJobs);
                Interlocked.Increment(ref this._inFlightJobs);
                this._lastDequeuedPriority = job.Priority;
                UpdateMaximum(
                    ref this._maxObservedQueueWaitMs,
                    GetElapsedMilliseconds(job.EnqueuedAtUtc, DateTime.UtcNow));
                var executionStopwatch = Stopwatch.StartNew();
                var trackExecutionSample = false;
                try
                {
                    using var activity = MonitorActivitySources.Scheduler.StartActivity(
                        "monitor.scheduler.execute_job",
                        ActivityKind.Internal);
                    activity?.SetTag("job.name", job.Name);
                    activity?.SetTag("job.priority", job.Priority.ToString());
                    activity?.SetTag("job.coalesced", !string.IsNullOrWhiteSpace(job.CoalesceKey));

                    this._logger.LogDebug("Executing scheduled job {JobName} ({Priority})", job.Name, job.Priority);
                    await job.Work(stoppingToken).ConfigureAwait(false);
                    Interlocked.Increment(ref this._executedJobs);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    trackExecutionSample = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref this._failedJobs);
                    Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    Activity.Current?.SetTag("error.type", ex.GetType().FullName);
                    Activity.Current?.SetTag("error.message", ex.Message);
                    this._logger.LogError(ex, "Scheduled job {JobName} failed", job.Name);
                    trackExecutionSample = true;
                }
                finally
                {
                    executionStopwatch.Stop();
                    if (trackExecutionSample)
                    {
                        Interlocked.Exchange(ref this._lastExecutionDurationMs, executionStopwatch.ElapsedMilliseconds);
                        Interlocked.Add(ref this._totalExecutionDurationMs, executionStopwatch.ElapsedMilliseconds);
                        Interlocked.Increment(ref this._completedExecutionSamples);
                    }

                    Interlocked.Decrement(ref this._inFlightJobs);
                    if (!string.IsNullOrWhiteSpace(job.CoalesceKey))
                    {
                        this._coalescedKeys.TryRemove(job.CoalesceKey, out _);
                        Interlocked.Increment(ref this._coalescedCompletedJobs);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown path.
        }
        finally
        {
            Task[] recurringTasks;
            lock (this._recurringLock)
            {
                this._isRunning = false;
                recurringTasks = this._recurringTasks.ToArray();
                this._recurringTasks.Clear();
            }

            try
            {
                await Task.WhenAll(recurringTasks).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this._logger.LogDebug(ex, "Recurring scheduler loops ended with cancellation/error.");
            }

            this._logger.LogInformation("Monitor job scheduler stopped");
        }
    }

    private static Task StartRecurringDelayAsync(TimeSpan initialDelay, CancellationToken cancellationToken)
    {
        return initialDelay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(initialDelay, cancellationToken);
    }

    private Task StartRecurringLoopAsync(RecurringJobRegistration registration, CancellationToken stoppingToken)
    {
        return Task.Run(
            async () =>
            {
                try
                {
                    await StartRecurringDelayAsync(registration.InitialDelay, stoppingToken).ConfigureAwait(false);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }

                    _ = this.Enqueue(
                        registration.Name,
                        registration.Work,
                        registration.Priority,
                        registration.CoalesceKey);

                    using var timer = new PeriodicTimer(registration.Interval);
                    while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                    {
                        _ = this.Enqueue(
                            registration.Name,
                            registration.Work,
                            registration.Priority,
                            registration.CoalesceKey);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown path.
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    this._logger.LogError(ex, "Recurring job loop failed for {JobName}", registration.Name);
                }
            },
            stoppingToken);
    }

    private static void UpdateMaximum(ref long target, long value)
    {
        while (true)
        {
            var current = Interlocked.Read(ref target);
            if (value <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private static long GetElapsedMilliseconds(DateTime startUtc, DateTime endUtc)
    {
        return Math.Max(0, (long)(endUtc - startUtc).TotalMilliseconds);
    }

    private long GetOldestQueuedJobAgeMs(DateTime nowUtc)
    {
        var high = GetOldestQueuedJobAgeMs(this._highPriorityQueue, nowUtc);
        var normal = GetOldestQueuedJobAgeMs(this._normalPriorityQueue, nowUtc);
        var low = GetOldestQueuedJobAgeMs(this._lowPriorityQueue, nowUtc);
        return Math.Max(high, Math.Max(normal, low));
    }

    private bool TryDequeueNext(out ScheduledJob job)
    {
        for (var attempts = 0; attempts < DispatchPattern.Length; attempts++)
        {
            var dispatchPatternIndex = this._dispatchPatternIndex;
            var priority = DispatchPattern[dispatchPatternIndex];
            this._dispatchPatternIndex = (dispatchPatternIndex + 1) % DispatchPattern.Length;

            if (this.TryDequeue(priority, out job))
            {
                return true;
            }
        }

        job = null!;
        return false;
    }

    private static long GetOldestQueuedJobAgeMs(IEnumerable<ScheduledJob> jobs, DateTime nowUtc)
    {
        long oldest = 0;
        foreach (var job in jobs)
        {
            oldest = Math.Max(oldest, GetElapsedMilliseconds(job.EnqueuedAtUtc, nowUtc));
        }

        return oldest;
    }

    private bool TryDequeue(MonitorJobPriority priority, out ScheduledJob job)
    {
        switch (priority)
        {
            case MonitorJobPriority.High:
                if (this._highPriorityQueue.TryDequeue(out var highJob) && highJob is not null)
                {
                    job = highJob;
                    return true;
                }

                break;
            case MonitorJobPriority.Low:
                if (this._lowPriorityQueue.TryDequeue(out var lowJob) && lowJob is not null)
                {
                    job = lowJob;
                    return true;
                }

                break;
            default:
                if (this._normalPriorityQueue.TryDequeue(out var normalJob) && normalJob is not null)
                {
                    job = normalJob;
                    return true;
                }

                break;
        }

        job = null!;
        return false;
    }

    private sealed record ScheduledJob(
        string Name,
        MonitorJobPriority Priority,
        Func<CancellationToken, Task> Work,
        string? CoalesceKey,
        DateTime EnqueuedAtUtc);

    private sealed record RecurringJobRegistration(
        string Name,
        TimeSpan Interval,
        TimeSpan InitialDelay,
        MonitorJobPriority Priority,
        Func<CancellationToken, Task> Work,
        string? CoalesceKey);
}
