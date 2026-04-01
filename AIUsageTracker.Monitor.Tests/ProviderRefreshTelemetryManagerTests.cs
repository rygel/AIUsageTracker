// <copyright file="ProviderRefreshTelemetryManagerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshTelemetryManagerTests
{
    private readonly ProviderRefreshTelemetryManager _service = new();

    [Fact]
    public void GetSnapshot_InitialState_IsZeroed()
    {
        var snapshot = this._service.GetSnapshot([]);

        Assert.Equal(0, snapshot.RefreshCount);
        Assert.Equal(0, snapshot.RefreshSuccessCount);
        Assert.Equal(0, snapshot.RefreshFailureCount);
        Assert.Equal(0, snapshot.ErrorRatePercent);
        Assert.Equal(0, snapshot.AverageLatencyMs);
        Assert.Equal(0, snapshot.LastLatencyMs);
        Assert.Null(snapshot.LastRefreshAttemptUtc);
        Assert.Null(snapshot.LastRefreshCompletedUtc);
        Assert.Null(snapshot.LastSuccessfulRefreshUtc);
        Assert.Null(snapshot.LastError);
        Assert.Empty(snapshot.ProviderDiagnostics);
        Assert.Empty(snapshot.OpenCircuitsByClassification);
    }

    [Fact]
    public void RecordRefreshAttemptStarted_SetsAttemptTimestamp()
    {
        var attemptUtc = new DateTime(2026, 03, 10, 15, 0, 0, DateTimeKind.Utc);

        this._service.RecordRefreshAttemptStarted(attemptUtc);

        var snapshot = this._service.GetSnapshot([]);
        Assert.Equal(attemptUtc, snapshot.LastRefreshAttemptUtc);
    }

    [Fact]
    public void RecordRefreshTelemetry_Failure_IncrementsFailureCountAndPreservesError()
    {
        var completedUtc = new DateTime(2026, 03, 10, 15, 1, 0, DateTimeKind.Utc);

        this._service.RecordRefreshTelemetry(TimeSpan.FromMilliseconds(45), success: false, "boom", completedUtc);

        var snapshot = this._service.GetSnapshot([]);
        Assert.Equal(1, snapshot.RefreshCount);
        Assert.Equal(0, snapshot.RefreshSuccessCount);
        Assert.Equal(1, snapshot.RefreshFailureCount);
        Assert.Equal(45, snapshot.LastLatencyMs);
        Assert.Equal(45, snapshot.AverageLatencyMs);
        Assert.Equal(100, snapshot.ErrorRatePercent);
        Assert.Equal(completedUtc, snapshot.LastRefreshCompletedUtc);
        Assert.Null(snapshot.LastSuccessfulRefreshUtc);
        Assert.Equal("boom", snapshot.LastError);
    }

    [Fact]
    public void RecordRefreshTelemetry_Success_UpdatesAverageLatencyAndClearsError()
    {
        this._service.RecordRefreshTelemetry(TimeSpan.FromMilliseconds(45), success: false, "boom");
        var completedUtc = new DateTime(2026, 03, 10, 15, 2, 0, DateTimeKind.Utc);

        this._service.RecordRefreshTelemetry(TimeSpan.FromMilliseconds(15), success: true, "ignored", completedUtc);

        var snapshot = this._service.GetSnapshot([]);
        Assert.Equal(2, snapshot.RefreshCount);
        Assert.Equal(1, snapshot.RefreshSuccessCount);
        Assert.Equal(1, snapshot.RefreshFailureCount);
        Assert.Equal(30, snapshot.AverageLatencyMs);
        Assert.Equal(15, snapshot.LastLatencyMs);
        Assert.Equal(50, snapshot.ErrorRatePercent);
        Assert.Equal(completedUtc, snapshot.LastRefreshCompletedUtc);
        Assert.Equal(completedUtc, snapshot.LastSuccessfulRefreshUtc);
        Assert.Null(snapshot.LastError);
    }

    // ── Phase 6: OpenCircuitsByClassification telemetry dimension ────────────

    [Fact]
    public void GetSnapshot_OpenCircuits_GroupedByClassification()
    {
        var diagnostics = new[]
        {
            new ProviderRefreshDiagnostic { ProviderId = "openai",    IsCircuitOpen = true,  LastFailureClassification = HttpFailureClassification.RateLimit },
            new ProviderRefreshDiagnostic { ProviderId = "anthropic", IsCircuitOpen = true,  LastFailureClassification = HttpFailureClassification.Server },
            new ProviderRefreshDiagnostic { ProviderId = "mistral",   IsCircuitOpen = true,  LastFailureClassification = HttpFailureClassification.RateLimit },
            new ProviderRefreshDiagnostic { ProviderId = "deepseek",  IsCircuitOpen = false, LastFailureClassification = HttpFailureClassification.Network },
        };

        var snapshot = this._service.GetSnapshot(diagnostics);

        Assert.Equal(2, snapshot.OpenCircuitsByClassification[HttpFailureClassification.RateLimit]);
        Assert.Equal(1, snapshot.OpenCircuitsByClassification[HttpFailureClassification.Server]);
        Assert.False(snapshot.OpenCircuitsByClassification.ContainsKey(HttpFailureClassification.Network));
    }

    [Fact]
    public void GetSnapshot_NoOpenCircuits_ReturnsEmptyClassificationSummary()
    {
        var diagnostics = new[]
        {
            new ProviderRefreshDiagnostic { ProviderId = "openai", IsCircuitOpen = false, LastFailureClassification = HttpFailureClassification.Server },
        };

        var snapshot = this._service.GetSnapshot(diagnostics);

        Assert.Empty(snapshot.OpenCircuitsByClassification);
    }

    [Fact]
    public void GetSnapshot_OpenCircuitsWithoutContext_ExcludedFromClassificationSummary()
    {
        // Provider without FailureContext attached — backward compat path
        var diagnostics = new[]
        {
            new ProviderRefreshDiagnostic { ProviderId = "legacy-provider", IsCircuitOpen = true, LastFailureClassification = null },
        };

        var snapshot = this._service.GetSnapshot(diagnostics);

        // Circuit IS open but excluded from the summary since no classification is known
        Assert.Empty(snapshot.OpenCircuitsByClassification);
    }
}
