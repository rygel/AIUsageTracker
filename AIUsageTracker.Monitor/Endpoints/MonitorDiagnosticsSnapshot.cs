// <copyright file="MonitorDiagnosticsSnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor.Endpoints;

internal sealed class MonitorDiagnosticsSnapshot
{
    public int Port { get; init; }

    public int ProcessId { get; init; }

    public string WorkingDir { get; init; } = string.Empty;

    public string BaseDir { get; init; } = string.Empty;

    public string StartedAt { get; init; } = string.Empty;

    public string Os { get; init; } = string.Empty;

    public string Runtime { get; init; } = string.Empty;

    public IReadOnlyList<string> Args { get; init; } = [];

    public IReadOnlyList<MonitorApiEndpointDescriptor> Endpoints { get; init; } = [];

    public RefreshTelemetrySnapshot RefreshTelemetry { get; init; } = new();

    public MonitorJobSchedulerSnapshot SchedulerTelemetry { get; init; } = new();

    public ProviderUsageProcessingTelemetrySnapshot PipelineTelemetry { get; init; } = new();

    public MonitorObservabilitySnapshot Observability { get; init; } = new();
}
