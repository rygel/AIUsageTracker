// <copyright file="RefreshTelemetrySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

public sealed class RefreshTelemetrySnapshot
{
    public long RefreshCount { get; init; }

    public long RefreshSuccessCount { get; init; }

    public long RefreshFailureCount { get; init; }

    public double ErrorRatePercent { get; init; }

    public double AverageLatencyMs { get; init; }

    public long LastLatencyMs { get; init; }

    public DateTime? LastRefreshAttemptUtc { get; init; }

    public DateTime? LastRefreshCompletedUtc { get; init; }

    public DateTime? LastSuccessfulRefreshUtc { get; init; }

    public string? LastError { get; init; }

    public IReadOnlyList<ProviderRefreshDiagnostic> ProviderDiagnostics { get; init; } =
        Array.Empty<ProviderRefreshDiagnostic>();
}
