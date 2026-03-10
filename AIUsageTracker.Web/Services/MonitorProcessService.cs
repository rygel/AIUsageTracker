// <copyright file="MonitorProcessService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Web.Services;

public class MonitorProcessService
{
    private readonly ILogger<MonitorProcessService> _logger;
    private readonly IMonitorService _monitorService;

    public readonly record struct MonitorStatusResult(
        bool IsRunning,
        int Port,
        string Message,
        string? Error,
        string? ServiceHealth,
        string? LastRefreshError,
        int ProvidersInBackoff,
        IReadOnlyList<string> FailingProviders,
        string? StartupState,
        string? StartupFailureReason);

    public readonly record struct MonitorActionResult(bool Success, string Message);

    public MonitorProcessService(ILogger<MonitorProcessService> logger, IMonitorService monitorService)
    {
        this._logger = logger;
        this._monitorService = monitorService;
    }

    public async Task<(bool IsRunning, int Port)> GetAgentStatusAsync()
    {
        var detailed = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
        return (detailed.IsRunning, detailed.Port);
    }

    public async Task<MonitorStatusResult> GetAgentStatusDetailedAsync()
    {
        var status = await MonitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (!status.IsRunning)
        {
            return CreateStatusResult(status, healthSnapshot: null);
        }

        try
        {
            await this._monitorService.RefreshAgentInfoAsync().ConfigureAwait(false);
            var healthSnapshot = await this._monitorService.GetHealthSnapshotAsync().ConfigureAwait(false);
            return CreateStatusResult(status, healthSnapshot);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to collect monitor health snapshot for web status API.");
            return CreateStatusResult(status, healthSnapshot: null);
        }
    }

    public async Task<bool> StartAgentAsync()
    {
        var detailed = await this.StartAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<MonitorActionResult> StartAgentDetailedAsync()
    {
        var status = await MonitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (status.IsRunning)
        {
            return new MonitorActionResult(true, $"Monitor already running on port {status.Port}.");
        }

        var started = await MonitorLauncher.EnsureAgentRunningAsync().ConfigureAwait(false);
        if (!started)
        {
            this._logger.LogWarning("Monitor failed to reach a healthy state after startup request.");
            return new MonitorActionResult(false, "Failed to start monitor or monitor did not become healthy.");
        }

        var updated = await MonitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (updated.IsRunning)
        {
            return new MonitorActionResult(true, $"Monitor started on port {updated.Port}.");
        }

        return new MonitorActionResult(false, $"Start requested, but monitor status is still unavailable. {updated.Message}");
    }

    public async Task<bool> StopAgentAsync()
    {
        var detailed = await this.StopAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<MonitorActionResult> StopAgentDetailedAsync()
    {
        var status = await MonitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (!status.IsRunning && string.Equals(status.Error, "agent-info-missing", StringComparison.Ordinal))
        {
            return new MonitorActionResult(true, "Monitor already stopped (info file missing).");
        }

        var stopped = await MonitorLauncher.StopAgentAsync().ConfigureAwait(false);
        if (stopped)
        {
            return new MonitorActionResult(true, $"Monitor stopped on port {status.Port}.");
        }

        this._logger.LogWarning("Monitor stop request failed.");
        return new MonitorActionResult(false, "Failed to stop monitor.");
    }

    private static string BuildRunningMessage(int port, MonitorHealthSnapshot healthSnapshot)
    {
        if (!string.Equals(healthSnapshot.ServiceHealth, "degraded", StringComparison.OrdinalIgnoreCase))
        {
            var lastSuccess = healthSnapshot.RefreshHealth.LastSuccessfulRefreshUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            return lastSuccess == null
                ? $"Healthy on port {port}."
                : $"Healthy on port {port}. Last successful refresh: {lastSuccess}.";
        }

        var providerSummary = healthSnapshot.RefreshHealth.FailingProviders.Count == 0
            ? string.Empty
            : $" Providers: {string.Join(", ", healthSnapshot.RefreshHealth.FailingProviders)}.";
        var errorSummary = string.IsNullOrWhiteSpace(healthSnapshot.RefreshHealth.LastError)
            ? string.Empty
            : $" Last error: {healthSnapshot.RefreshHealth.LastError}.";
        var backoffSummary = healthSnapshot.RefreshHealth.ProvidersInBackoff > 0
            ? $" Backoff: {healthSnapshot.RefreshHealth.ProvidersInBackoff} provider(s)."
            : string.Empty;
        return $"Running on port {port}, but refresh health is degraded.{providerSummary}{errorSummary}{backoffSummary}";
    }

    private static MonitorStatusResult CreateStatusResult(
        MonitorLauncher.MonitorStatusInfo status,
        MonitorHealthSnapshot? healthSnapshot)
    {
        var startupState = GetStartupState(status.Error);
        var startupFailureReason = GetStartupFailureReason(status);
        if (healthSnapshot == null)
        {
            return new MonitorStatusResult(
                status.IsRunning,
                status.Port,
                status.Message,
                status.Error,
                null,
                null,
                0,
                Array.Empty<string>(),
                startupState,
                startupFailureReason);
        }

        return new MonitorStatusResult(
            status.IsRunning,
            status.Port,
            BuildRunningMessage(status.Port, healthSnapshot),
            status.Error,
            healthSnapshot.ServiceHealth,
            healthSnapshot.RefreshHealth.LastError,
            healthSnapshot.RefreshHealth.ProvidersInBackoff,
            healthSnapshot.RefreshHealth.FailingProviders,
            startupState,
            startupFailureReason);
    }

    private static string? GetStartupState(string? error)
    {
        return error switch
        {
            "monitor-starting" => "starting",
            "monitor-startup-failed" => "failed",
            _ => null,
        };
    }

    private static string? GetStartupFailureReason(MonitorLauncher.MonitorStatusInfo status)
    {
        if (!string.Equals(status.Error, "monitor-startup-failed", StringComparison.Ordinal))
        {
            return null;
        }

        var failureReason = status.Message?.Trim();
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return null;
        }

        const string startupPrefix = "Startup status:";
        if (failureReason.StartsWith(startupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = failureReason[startupPrefix.Length..].Trim();
        }

        const string failedPrefix = "failed:";
        if (failureReason.StartsWith(failedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = failureReason[failedPrefix.Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(failureReason) ? null : failureReason;
    }
}
