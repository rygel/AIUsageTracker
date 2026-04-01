// <copyright file="RefreshTelemetrySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

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

    /// <summary>
    /// Gets the count of currently open circuits grouped by the failure classification that
    /// caused them, for providers that attach <c>HttpFailureContext</c>. Providers that have
    /// not yet adopted structured failure context are excluded (their circuits appear in
    /// <see cref="ProviderDiagnostics"/> with a null <c>LastFailureClassification</c>).
    /// Empty when no circuits are currently open with a known classification.
    /// </summary>
    public IReadOnlyDictionary<HttpFailureClassification, int> OpenCircuitsByClassification { get; init; } =
        new Dictionary<HttpFailureClassification, int>();
}
