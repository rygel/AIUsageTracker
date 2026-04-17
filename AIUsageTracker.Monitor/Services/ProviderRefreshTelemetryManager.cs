// <copyright file="ProviderRefreshTelemetryManager.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

internal sealed class ProviderRefreshTelemetryManager
{
    private readonly object _syncLock = new();
    private long _refreshCount;
    private long _refreshFailureCount;
    private long _refreshTotalLatencyMs;
    private long _lastRefreshLatencyMs;
    private DateTime? _lastRefreshAttemptUtc;
    private DateTime? _lastRefreshCompletedUtc;
    private DateTime? _lastSuccessfulRefreshUtc;
    private string? _lastRefreshError;

    public RefreshTelemetrySnapshot GetSnapshot(IReadOnlyList<ProviderRefreshDiagnostic> providerDiagnostics)
    {
        var refreshCount = Interlocked.Read(ref this._refreshCount);
        var refreshFailureCount = Interlocked.Read(ref this._refreshFailureCount);
        var refreshTotalLatencyMs = Interlocked.Read(ref this._refreshTotalLatencyMs);
        var lastRefreshLatencyMs = Interlocked.Read(ref this._lastRefreshLatencyMs);

        DateTime? lastRefreshCompletedUtc;
        DateTime? lastRefreshAttemptUtc;
        DateTime? lastSuccessfulRefreshUtc;
        string? lastRefreshError;
        lock (this._syncLock)
        {
            lastRefreshAttemptUtc = this._lastRefreshAttemptUtc;
            lastRefreshCompletedUtc = this._lastRefreshCompletedUtc;
            lastSuccessfulRefreshUtc = this._lastSuccessfulRefreshUtc;
            lastRefreshError = this._lastRefreshError;
        }

        var refreshSuccessCount = Math.Max(0, refreshCount - refreshFailureCount);
        var averageLatencyMs = refreshCount == 0 ? 0 : refreshTotalLatencyMs / (double)refreshCount;
        var errorRatePercent = refreshCount == 0 ? 0 : (refreshFailureCount / (double)refreshCount) * 100.0;

        var openCircuitsByClassification = providerDiagnostics
            .Where(d => d.IsCircuitOpen && d.LastFailureClassification.HasValue)
            .GroupBy(d => d.LastFailureClassification!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return new RefreshTelemetrySnapshot
        {
            RefreshCount = refreshCount,
            RefreshSuccessCount = refreshSuccessCount,
            RefreshFailureCount = refreshFailureCount,
            ErrorRatePercent = errorRatePercent,
            AverageLatencyMs = averageLatencyMs,
            LastLatencyMs = lastRefreshLatencyMs,
            LastRefreshAttemptUtc = lastRefreshAttemptUtc,
            LastRefreshCompletedUtc = lastRefreshCompletedUtc,
            LastSuccessfulRefreshUtc = lastSuccessfulRefreshUtc,
            LastError = lastRefreshError,
            ProviderDiagnostics = providerDiagnostics,
            OpenCircuitsByClassification = openCircuitsByClassification,
        };
    }

    public void RecordRefreshAttemptStarted(DateTime attemptUtc)
    {
        lock (this._syncLock)
        {
            this._lastRefreshAttemptUtc = attemptUtc;
        }
    }

    public void RecordRefreshTelemetry(TimeSpan duration, bool success, string? errorMessage, DateTime? completedUtc = null)
    {
        var latencyMs = (long)Math.Max(0, duration.TotalMilliseconds);

        Interlocked.Increment(ref this._refreshCount);
        Interlocked.Add(ref this._refreshTotalLatencyMs, latencyMs);
        Interlocked.Exchange(ref this._lastRefreshLatencyMs, latencyMs);

        if (!success)
        {
            Interlocked.Increment(ref this._refreshFailureCount);
        }

        lock (this._syncLock)
        {
            this._lastRefreshCompletedUtc = completedUtc ?? DateTime.UtcNow;
            if (success)
            {
                this._lastSuccessfulRefreshUtc = this._lastRefreshCompletedUtc;
            }

            this._lastRefreshError = success ? null : errorMessage;
        }
    }
}
