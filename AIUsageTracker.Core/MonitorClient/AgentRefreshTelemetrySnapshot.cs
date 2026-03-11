// <copyright file="AgentRefreshTelemetrySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.MonitorClient;

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
