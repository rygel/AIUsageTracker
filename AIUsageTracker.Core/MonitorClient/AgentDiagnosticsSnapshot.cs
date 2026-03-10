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

    [JsonPropertyName("observability")]
    public AgentObservabilitySnapshot? Observability { get; init; }
}
