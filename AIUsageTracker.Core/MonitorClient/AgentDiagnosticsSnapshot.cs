// <copyright file="AgentDiagnosticsSnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentDiagnosticsSnapshot
{
    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("process_id")]
    public int ProcessId { get; init; }

    [JsonPropertyName("working_dir")]
    public string WorkingDirectory { get; init; } = string.Empty;

    [JsonPropertyName("base_dir")]
    public string BaseDirectory { get; init; } = string.Empty;

    [JsonPropertyName("started_at")]
    public string StartedAt { get; init; } = string.Empty;

    [JsonPropertyName("os")]
    public string Os { get; init; } = string.Empty;

    [JsonPropertyName("runtime")]
    public string Runtime { get; init; } = string.Empty;

    [JsonPropertyName("args")]
    public IReadOnlyList<string> Args { get; init; } = [];

    [JsonPropertyName("endpoints")]
    public IReadOnlyList<AgentEndpointDescriptor> Endpoints { get; init; } = [];

    [JsonPropertyName("refresh_telemetry")]
    public AgentRefreshTelemetrySnapshot? RefreshTelemetry { get; init; }

    [JsonPropertyName("scheduler_telemetry")]
    public AgentSchedulerTelemetrySnapshot? SchedulerTelemetry { get; init; }

    [JsonPropertyName("pipeline_telemetry")]
    public AgentPipelineTelemetrySnapshot? PipelineTelemetry { get; init; }
}

public sealed class AgentEndpointDescriptor
{
    [JsonPropertyName("route")]
    public string Route { get; init; } = string.Empty;

    [JsonPropertyName("methods")]
    public IReadOnlyList<string> Methods { get; init; } = [];
}

public sealed class AgentRefreshTelemetrySnapshot
{
    [JsonPropertyName("refresh_count")]
    public long RefreshCount { get; init; }

    [JsonPropertyName("refresh_success_count")]
    public long RefreshSuccessCount { get; init; }

    [JsonPropertyName("refresh_failure_count")]
    public long RefreshFailureCount { get; init; }

    [JsonPropertyName("error_rate_percent")]
    public double ErrorRatePercent { get; init; }

    [JsonPropertyName("average_latency_ms")]
    public double AverageLatencyMs { get; init; }

    [JsonPropertyName("last_latency_ms")]
    public long LastLatencyMs { get; init; }

    [JsonPropertyName("last_refresh_completed_utc")]
    public DateTime? LastRefreshCompletedUtc { get; init; }

    [JsonPropertyName("last_error")]
    public string? LastError { get; init; }
}

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

public sealed class AgentPipelineTelemetrySnapshot
{
    [JsonPropertyName("total_processed_entries")]
    public long TotalProcessedEntries { get; init; }

    [JsonPropertyName("total_accepted_entries")]
    public long TotalAcceptedEntries { get; init; }

    [JsonPropertyName("total_rejected_entries")]
    public long TotalRejectedEntries { get; init; }

    [JsonPropertyName("invalid_identity_count")]
    public long InvalidIdentityCount { get; init; }

    [JsonPropertyName("inactive_provider_filtered_count")]
    public long InactiveProviderFilteredCount { get; init; }

    [JsonPropertyName("placeholder_filtered_count")]
    public long PlaceholderFilteredCount { get; init; }

    [JsonPropertyName("detail_contract_adjusted_count")]
    public long DetailContractAdjustedCount { get; init; }

    [JsonPropertyName("normalized_count")]
    public long NormalizedCount { get; init; }

    [JsonPropertyName("privacy_redacted_count")]
    public long PrivacyRedactedCount { get; init; }

    [JsonPropertyName("last_processed_at_utc")]
    public DateTime? LastProcessedAtUtc { get; init; }

    [JsonPropertyName("last_run_total_entries")]
    public int LastRunTotalEntries { get; init; }

    [JsonPropertyName("last_run_accepted_entries")]
    public int LastRunAcceptedEntries { get; init; }
}
