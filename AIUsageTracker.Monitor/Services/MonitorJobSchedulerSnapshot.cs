// <copyright file="MonitorJobSchedulerSnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

public sealed class MonitorJobSchedulerSnapshot
{
    public bool IsPaused { get; init; }

    public int HighPriorityQueuedJobs { get; init; }

    public int NormalPriorityQueuedJobs { get; init; }

    public int LowPriorityQueuedJobs { get; init; }

    public int TotalQueuedJobs { get; init; }

    public int RecurringJobs { get; init; }

    public long ExecutedJobs { get; init; }

    public long FailedJobs { get; init; }

    public long EnqueuedJobs { get; init; }

    public long DequeuedJobs { get; init; }

    public long CoalescedSkippedJobs { get; init; }

    public long CoalescedCompletedJobs { get; init; }

    public long DispatchNoopSignals { get; init; }

    public long InFlightJobs { get; init; }

    public long OldestQueuedJobAgeMs { get; init; }

    public long MaxObservedQueueWaitMs { get; init; }

    public long AverageExecutionDurationMs { get; init; }

    public long LastExecutionDurationMs { get; init; }

    public string? LastDequeuedPriority { get; init; }

    public string? NextDispatchPriority { get; init; }
}
