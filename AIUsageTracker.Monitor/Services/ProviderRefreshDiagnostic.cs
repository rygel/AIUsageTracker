// <copyright file="ProviderRefreshDiagnostic.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderRefreshDiagnostic
{
    public string ProviderId { get; init; } = string.Empty;

    public DateTime? LastRefreshAttemptUtc { get; init; }

    public DateTime? LastSuccessfulRefreshUtc { get; init; }

    public string? LastRefreshError { get; init; }

    public bool IsCircuitOpen { get; init; }

    public DateTime? CircuitOpenUntilUtc { get; init; }

    public int ConsecutiveFailures { get; init; }
}
