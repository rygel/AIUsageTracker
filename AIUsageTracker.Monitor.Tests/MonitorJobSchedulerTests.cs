// <copyright file="MonitorJobSchedulerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Diagnostics;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class MonitorJobSchedulerTests
{
    [Fact]
    public async Task Enqueue_HighPriorityRunsBeforeLowPriorityAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var executionOrder = new ConcurrentQueue<string>();
        var completionSignal = new SemaphoreSlim(0, 2);

        _ = scheduler.Enqueue(
            "low-priority-job",
            _ =>
            {
                executionOrder.Enqueue("low");
                completionSignal.Release();
                return Task.CompletedTask;
            },
            MonitorJobPriority.Low);

        _ = scheduler.Enqueue(
            "high-priority-job",
            _ =>
            {
                executionOrder.Enqueue("high");
                completionSignal.Release();
                return Task.CompletedTask;
            },
            MonitorJobPriority.High);

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await completionSignal.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(await completionSignal.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.True(executionOrder.TryDequeue(out var first));
            Assert.Equal("high", first);

            var snapshot = scheduler.GetSnapshot();
            Assert.Equal(2, snapshot.EnqueuedJobs);
            Assert.Equal(2, snapshot.DequeuedJobs);
            Assert.Equal(0, snapshot.CoalescedSkippedJobs);
            Assert.Equal(0, snapshot.InFlightJobs);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RegisterRecurringJob_ExecutesScheduledWorkAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var firstRun = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scheduler.RegisterRecurringJob(
            "recurring-test-job",
            TimeSpan.FromMilliseconds(50),
            _ =>
            {
                firstRun.TrySetResult(true);
                return Task.CompletedTask;
            },
            MonitorJobPriority.Normal,
            initialDelay: TimeSpan.FromMilliseconds(10));

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            var completed = await Task.WhenAny(firstRun.Task, Task.Delay(TimeSpan.FromSeconds(2))) == firstRun.Task;
            Assert.True(completed, "Recurring job did not execute within timeout.");

            var snapshot = scheduler.GetSnapshot();
            Assert.Equal(1, snapshot.RecurringJobs);
            Assert.True(snapshot.ExecutedJobs >= 1);
            Assert.True(snapshot.EnqueuedJobs >= 1);
            Assert.True(snapshot.DequeuedJobs >= 1);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Enqueue_WithCoalesceKey_DeduplicatesPendingJobsAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var executionCount = 0;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstQueued = scheduler.Enqueue(
            "coalesced-job-1",
            _ =>
            {
                if (Interlocked.Increment(ref executionCount) == 1)
                {
                    completion.TrySetResult(true);
                }

                return Task.CompletedTask;
            },
            MonitorJobPriority.Normal,
            coalesceKey: "refresh-key");

        var secondQueued = scheduler.Enqueue(
            "coalesced-job-2",
            _ =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.CompletedTask;
            },
            MonitorJobPriority.High,
            coalesceKey: "refresh-key");

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            var completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(2))) == completion.Task;
            Assert.True(completed, "Expected first coalesced job to execute.");
            Assert.True(firstQueued);
            Assert.False(secondQueued);
            Assert.Equal(1, executionCount);

            var snapshot = await WaitForCoalescedCompletionAsync(scheduler, expectedCompletedJobs: 1);

            Assert.Equal(1, snapshot.EnqueuedJobs);
            Assert.Equal(1, snapshot.DequeuedJobs);
            Assert.Equal(1, snapshot.CoalescedSkippedJobs);
            Assert.Equal(1, snapshot.CoalescedCompletedJobs);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopAsync_StopsRecurringJobExecutionAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var firstRun = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runCount = 0;

        scheduler.RegisterRecurringJob(
            "stop-behavior-job",
            TimeSpan.FromMilliseconds(20),
            _ =>
            {
                if (Interlocked.Increment(ref runCount) == 1)
                {
                    firstRun.TrySetResult(true);
                }

                return Task.CompletedTask;
            },
            MonitorJobPriority.Normal,
            initialDelay: TimeSpan.FromMilliseconds(5));

        await scheduler.StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(firstRun.Task, Task.Delay(TimeSpan.FromSeconds(2))) == firstRun.Task;
        Assert.True(completed, "Recurring job did not run before stop.");

        await scheduler.StopAsync(CancellationToken.None);
        var executedAfterStop = scheduler.GetSnapshot().ExecutedJobs;

        await Task.Delay(150);

        Assert.Equal(executedAfterStop, scheduler.GetSnapshot().ExecutedJobs);
    }

    [Fact]
    public async Task Enqueue_EmitsExecutionActivityWithJobMetadataAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedActivities = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, MonitorActivitySources.SchedulerSourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stoppedActivities.Enqueue(activity),
        };

        ActivitySource.AddActivityListener(listener);

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            _ = scheduler.Enqueue(
                "activity-job",
                _ =>
                {
                    completion.TrySetResult(true);
                    return Task.CompletedTask;
                },
                MonitorJobPriority.High);

            var completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(3))) == completion.Task;
            Assert.True(completed, "Scheduled job did not complete within timeout.");
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }

        var jobActivity = Assert.Single(
            stoppedActivities,
            activity => string.Equals(activity.OperationName, "monitor.scheduler.execute_job", StringComparison.Ordinal));
        Assert.Equal("activity-job", jobActivity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, "job.name", StringComparison.Ordinal)).Value);
        Assert.Equal("High", jobActivity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, "job.priority", StringComparison.Ordinal)).Value);
        Assert.False((bool)jobActivity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, "job.coalesced", StringComparison.Ordinal)).Value!);
        Assert.Equal(ActivityStatusCode.Ok, jobActivity.Status);
    }

    [Fact]
    public async Task Enqueue_LowPriorityDoesNotStarve_WhenHighPriorityKeepsArrivingAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var lowPriorityCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remainingHighJobs = 1000;

        Func<CancellationToken, Task>? highPriorityWork = null;
        highPriorityWork = async cancellationToken =>
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            if (Interlocked.Decrement(ref remainingHighJobs) > 0)
            {
                _ = scheduler.Enqueue("high-priority-loop", highPriorityWork!, MonitorJobPriority.High);
            }
        };

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            _ = scheduler.Enqueue("high-priority-seed", highPriorityWork, MonitorJobPriority.High);
            _ = scheduler.Enqueue(
                "low-priority-seed",
                _ =>
                {
                    lowPriorityCompleted.TrySetResult(true);
                    return Task.CompletedTask;
                },
                MonitorJobPriority.Low);

            var completed = await Task.WhenAny(lowPriorityCompleted.Task, Task.Delay(TimeSpan.FromSeconds(2))) == lowPriorityCompleted.Task;
            Assert.True(completed, "Low-priority work should not starve behind continuously-arriving high-priority jobs.");

            var snapshot = scheduler.GetSnapshot();
            Assert.True(snapshot.DequeuedJobs >= 2);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GetSnapshot_ReportsQueuedAgeAndExecutionTimingAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = scheduler.Enqueue(
            "timing-job",
            async ct =>
            {
                await Task.Delay(60, ct).ConfigureAwait(false);
                completion.TrySetResult(true);
            },
            MonitorJobPriority.Normal);

        await Task.Delay(80);
        var queuedSnapshot = scheduler.GetSnapshot();
        Assert.True(queuedSnapshot.OldestQueuedJobAgeMs >= 50);
        Assert.Equal("High", queuedSnapshot.NextDispatchPriority);

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            var completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(2))) == completion.Task;
            Assert.True(completed, "Timed job did not complete within timeout.");

            MonitorJobSchedulerSnapshot snapshot;
            var deadline = DateTime.UtcNow.AddSeconds(2);
            do
            {
                snapshot = scheduler.GetSnapshot();
                if (snapshot.LastExecutionDurationMs > 0 &&
                    snapshot.AverageExecutionDurationMs > 0 &&
                    string.Equals(snapshot.LastDequeuedPriority, "Normal", StringComparison.Ordinal))
                {
                    break;
                }

                await Task.Delay(10);
            }
            while (DateTime.UtcNow < deadline);

            Assert.True(snapshot.LastExecutionDurationMs >= 20);
            Assert.True(snapshot.AverageExecutionDurationMs > 0);
            Assert.True(snapshot.AverageExecutionDurationMs <= snapshot.LastExecutionDurationMs);
            Assert.True(snapshot.MaxObservedQueueWaitMs >= 30);
            Assert.Equal("Normal", snapshot.LastDequeuedPriority);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Pause_BlocksNewJobDispatchWhileInFlightCompletesAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var firstJobStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstJobCanFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondJobExecuted = false;

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            // Enqueue a long-running first job that signals when started
            _ = scheduler.Enqueue(
                "in-flight-job",
                async _ =>
                {
                    firstJobStarted.TrySetResult(true);
                    var finishTask = firstJobCanFinish.Task;
                    await finishTask.ConfigureAwait(false);
                },
                MonitorJobPriority.Normal);

            // Wait for first job to start
            var started = await Task.WhenAny(firstJobStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))) == firstJobStarted.Task;
            Assert.True(started, "First job did not start within timeout.");

            // Pause while first job is in-flight
            scheduler.Pause();

            // Verify snapshot reports paused
            Assert.True(scheduler.GetSnapshot().IsPaused);

            // Enqueue a second job - it should not execute while paused
            _ = scheduler.Enqueue(
                "blocked-job",
                _ =>
                {
                    secondJobExecuted = true;
                    return Task.CompletedTask;
                },
                MonitorJobPriority.Normal);

            // Let the first in-flight job finish
            firstJobCanFinish.TrySetResult(true);

            // Wait a moment — second job should NOT run while paused
            await Task.Delay(200);

            Assert.False(secondJobExecuted, "Second job should not execute while scheduler is paused.");
            Assert.True(scheduler.GetSnapshot().IsPaused);
        }
        finally
        {
            scheduler.Resume();
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Resume_RestoresDispatchAfterPauseAsync()
    {
        var logger = new Mock<ILogger<MonitorJobScheduler>>();
        var scheduler = new MonitorJobScheduler(logger.Object);
        var jobExecuted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            // Pause before enqueuing
            scheduler.Pause();
            Assert.True(scheduler.GetSnapshot().IsPaused);

            // Enqueue a job while paused — it should not run yet
            _ = scheduler.Enqueue(
                "held-job",
                _ =>
                {
                    jobExecuted.TrySetResult(true);
                    return Task.CompletedTask;
                },
                MonitorJobPriority.Normal);

            // Verify job does not run within a short wait
            var ranBeforeResume = await Task.WhenAny(jobExecuted.Task, Task.Delay(200)) == jobExecuted.Task;
            Assert.False(ranBeforeResume, "Job should not run while paused.");

            // Resume
            scheduler.Resume();
            Assert.False(scheduler.GetSnapshot().IsPaused);

            // Job should now run
            var ranAfterResume = await Task.WhenAny(jobExecuted.Task, Task.Delay(TimeSpan.FromSeconds(5))) == jobExecuted.Task;
            Assert.True(ranAfterResume, "Job did not run after resume within timeout.");
            Assert.False(scheduler.GetSnapshot().IsPaused);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<MonitorJobSchedulerSnapshot> WaitForCoalescedCompletionAsync(
        MonitorJobScheduler scheduler,
        int expectedCompletedJobs)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        MonitorJobSchedulerSnapshot snapshot;
        do
        {
            snapshot = scheduler.GetSnapshot();
            if (snapshot.CoalescedCompletedJobs == expectedCompletedJobs)
            {
                return snapshot;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return snapshot;
    }
}
