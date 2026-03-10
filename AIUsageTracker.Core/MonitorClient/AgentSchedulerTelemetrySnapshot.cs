// <copyright file="AgentSchedulerTelemetrySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentSchedulerTelemetrySnapshot
{
    [JsonPropertyName("high_priority_queued_jobs")]
    public int HighPriorityQueuedJobs { get; init; }

    [JsonPropertyName("normal_priority_queued_jobs")]
    public int NormalPriorityQueuedJobs { get; init; }

    [JsonPropertyName("low_priority_queued_jobs")]
    public int LowPriorityQueuedJobs { get; init; }

    [JsonPropertyName("total_queued_jobs")]
    public int TotalQueuedJobs { get; init; }

    [JsonPropertyName("recurring_jobs")]
    public int RecurringJobs { get; init; }

    [JsonPropertyName("executed_jobs")]
    public long ExecutedJobs { get; init; }

    [JsonPropertyName("failed_jobs")]
    public long FailedJobs { get; init; }

    [JsonPropertyName("enqueued_jobs")]
    public long EnqueuedJobs { get; init; }

    [JsonPropertyName("dequeued_jobs")]
    public long DequeuedJobs { get; init; }

    [JsonPropertyName("coalesced_skipped_jobs")]
    public long CoalescedSkippedJobs { get; init; }

    [JsonPropertyName("dispatch_noop_signals")]
    public long DispatchNoopSignals { get; init; }

    [JsonPropertyName("in_flight_jobs")]
    public long InFlightJobs { get; init; }
}
