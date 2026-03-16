// <copyright file="AgentTelemetrySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentTelemetrySnapshot
{
    public long UsageRequestCount { get; init; }

    public long UsageErrorCount { get; init; }

    public double UsageAverageLatencyMs { get; init; }

    public long UsageLastLatencyMs { get; init; }

    public double UsageErrorRatePercent { get; init; }

    public long RefreshRequestCount { get; init; }

    public long RefreshErrorCount { get; init; }

    public double RefreshAverageLatencyMs { get; init; }

    public long RefreshLastLatencyMs { get; init; }

    public double RefreshErrorRatePercent { get; init; }
}
