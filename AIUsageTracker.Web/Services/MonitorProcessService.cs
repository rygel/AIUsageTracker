// <copyright file="MonitorProcessService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Models;
using System.Diagnostics;

namespace AIUsageTracker.Web.Services;

public class MonitorProcessService
{
    private static readonly ActivitySource ActivitySource = new("AIUsageTracker.Web.MonitorProcessService");
    private readonly ILogger<MonitorProcessService> _logger;
    private readonly IMonitorService _monitorService;
    private readonly IMonitorLauncherClient _monitorLauncherClient;

    public readonly record struct MonitorStatusResult(
        bool IsRunning,
        int Port,
        string Message,
        string? Error,
        string? ServiceHealth,
        string? LastRefreshError,
        int ProvidersInBackoff,
        IReadOnlyList<string> FailingProviders,
        bool? IsContractCompatible,
        string? ContractVersion,
        string? MinClientContractVersion,
        string? ContractMessage,
        string? StartupState,
        string? StartupFailureReason);

    public readonly record struct MonitorActionResult(
        bool Success,
        string Message,
        string? Error,
        string? StartupState,
        string? StartupFailureReason);

    public MonitorProcessService(ILogger<MonitorProcessService> logger, IMonitorService monitorService)
        : this(logger, monitorService, new MonitorLauncherClient())
    {
    }

    public MonitorProcessService(
        ILogger<MonitorProcessService> logger,
        IMonitorService monitorService,
        IMonitorLauncherClient monitorLauncherClient)
    {
        this._logger = logger;
        this._monitorService = monitorService;
        this._monitorLauncherClient = monitorLauncherClient;
    }

    public async Task<(bool IsRunning, int Port)> GetAgentStatusAsync()
    {
        var detailed = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
        return (detailed.IsRunning, detailed.Port);
    }

    public async Task<MonitorStatusResult> GetAgentStatusDetailedAsync()
    {
        using var activity = ActivitySource.StartActivity("web.monitor.get_status", ActivityKind.Internal);
        var status = await this._monitorLauncherClient.GetAgentStatusInfoAsync().ConfigureAwait(false);
        activity?.SetTag("monitor.is_running", status.IsRunning);
        activity?.SetTag("monitor.port", status.Port);
        activity?.SetTag("monitor.error", status.Error);
        if (!status.IsRunning)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return CreateStatusResult(status, healthSnapshot: null, contractHandshake: null);
        }

        try
        {
            await this._monitorService.RefreshAgentInfoAsync().ConfigureAwait(false);
            var healthSnapshot = await this._monitorService.GetHealthSnapshotAsync().ConfigureAwait(false);
            var contractVersion = healthSnapshot?.EffectiveContractVersion;
            var minClientContractVersion = healthSnapshot?.EffectiveMinClientContractVersion;
            var contractHandshake = MonitorApiContractEvaluator.Evaluate(
                contractVersion,
                minClientContractVersion,
                healthSnapshot?.AgentVersion,
                MonitorApiContract.CurrentVersion);
            activity?.SetTag("monitor.service_health", healthSnapshot?.ServiceHealth);
            activity?.SetTag("monitor.contract_compatible", contractHandshake.IsCompatible);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return CreateStatusResult(status, healthSnapshot, contractHandshake);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to collect monitor health snapshot for web status API.");
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return CreateStatusResult(status, healthSnapshot: null, contractHandshake: null);
        }
    }

    public async Task<bool> StartAgentAsync()
    {
        var detailed = await this.StartAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<MonitorActionResult> StartAgentDetailedAsync()
    {
        using var activity = ActivitySource.StartActivity("web.monitor.start", ActivityKind.Internal);
        var status = await this._monitorLauncherClient.GetAgentStatusInfoAsync().ConfigureAwait(false);
        activity?.SetTag("monitor.is_running_before", status.IsRunning);
        activity?.SetTag("monitor.port_before", status.Port);
        if (status.IsRunning)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new MonitorActionResult(
                true,
                $"Monitor already running on port {status.Port}.",
                null,
                null,
                null);
        }

        var started = await this._monitorLauncherClient.EnsureAgentRunningAsync().ConfigureAwait(false);
        activity?.SetTag("monitor.start_requested", true);
        activity?.SetTag("monitor.start_result", started);
        if (!started)
        {
            this._logger.LogWarning("Monitor failed to reach a healthy state after startup request.");
            var failedStatus = await this._monitorLauncherClient.GetAgentStatusInfoAsync().ConfigureAwait(false);
            activity?.SetTag("monitor.error", failedStatus.Error);
            activity?.SetStatus(ActivityStatusCode.Error, "Monitor failed to start");
            return CreateStartFailureResult(
                failedStatus,
                "Failed to start monitor or monitor did not become healthy.",
                GetRecentStartupFailureReason());
        }

        var updated = await this._monitorLauncherClient.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (updated.IsRunning)
        {
            activity?.SetTag("monitor.port_after", updated.Port);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new MonitorActionResult(
                true,
                $"Monitor started on port {updated.Port}.",
                null,
                null,
                null);
        }

        activity?.SetTag("monitor.error", updated.Error);
        activity?.SetStatus(ActivityStatusCode.Error, "Monitor status unavailable after startup");
        return CreateStartFailureResult(
            updated,
            $"Start requested, but monitor status is still unavailable. {updated.Message}",
            GetRecentStartupFailureReason());
    }

    public async Task<bool> StopAgentAsync()
    {
        var detailed = await this.StopAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<MonitorActionResult> StopAgentDetailedAsync()
    {
        using var activity = ActivitySource.StartActivity("web.monitor.stop", ActivityKind.Internal);
        var status = await this._monitorLauncherClient.GetAgentStatusInfoAsync().ConfigureAwait(false);
        activity?.SetTag("monitor.is_running_before", status.IsRunning);
        activity?.SetTag("monitor.port_before", status.Port);
        if (!status.IsRunning && string.Equals(status.Error, "agent-info-missing", StringComparison.Ordinal))
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new MonitorActionResult(
                true,
                "Monitor already stopped (info file missing).",
                null,
                null,
                null);
        }

        var stopped = await this._monitorLauncherClient.StopAgentAsync().ConfigureAwait(false);
        activity?.SetTag("monitor.stop_result", stopped);
        if (stopped)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new MonitorActionResult(
                true,
                $"Monitor stopped on port {status.Port}.",
                null,
                null,
                null);
        }

        this._logger.LogWarning("Monitor stop request failed.");
        activity?.SetStatus(ActivityStatusCode.Error, "Monitor stop failed");
        return new MonitorActionResult(false, "Failed to stop monitor.", null, null, null);
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
        MonitorAgentStatus status,
        MonitorHealthSnapshot? healthSnapshot,
        AgentContractHandshakeResult? contractHandshake)
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
                null,
                null,
                null,
                null,
                startupState,
                startupFailureReason);
        }

        var message = BuildRunningMessage(status.Port, healthSnapshot);
        if (contractHandshake is { IsReachable: true, IsCompatible: false } &&
            !string.IsNullOrWhiteSpace(contractHandshake.Message))
        {
            message = $"{message} Contract warning: {contractHandshake.Message}";
        }

        return new MonitorStatusResult(
            status.IsRunning,
            status.Port,
            message,
            status.Error,
            healthSnapshot.ServiceHealth,
            healthSnapshot.RefreshHealth.LastError,
            healthSnapshot.RefreshHealth.ProvidersInBackoff,
            healthSnapshot.RefreshHealth.FailingProviders,
            contractHandshake?.IsCompatible,
            contractHandshake?.AgentContractVersion,
            contractHandshake?.MinClientContractVersion,
            contractHandshake?.Message,
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

    private static string? GetStartupFailureReason(MonitorAgentStatus status)
    {
        if (!string.Equals(status.Error, "monitor-startup-failed", StringComparison.Ordinal))
        {
            return null;
        }

        return NormalizeStartupFailureReason(status.Message);
    }

    private static MonitorActionResult CreateStartFailureResult(
        MonitorAgentStatus status,
        string fallbackMessage,
        string? preservedStartupFailureReason = null)
    {
        var startupState = GetStartupState(status.Error);
        var startupFailureReason = GetStartupFailureReason(status) ?? NormalizeStartupFailureReason(preservedStartupFailureReason);
        var error = status.Error;
        if (!string.IsNullOrWhiteSpace(startupFailureReason))
        {
            startupState ??= "failed";
            error = "monitor-startup-failed";
        }

        var message = status.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = fallbackMessage;
        }
        else if (!string.IsNullOrWhiteSpace(startupFailureReason))
        {
            message = $"Monitor startup failed: {startupFailureReason}";
        }

        return new MonitorActionResult(
            false,
            message,
            error,
            startupState,
            startupFailureReason);
    }

    private static string? GetRecentStartupFailureReason()
    {
        var diagnostics = MonitorService.DiagnosticsLog;
        const string startupFailurePrefix = "Monitor startup failed after";
        const string reportedFailurePrefix = "Monitor startup reported failure:";
        for (var index = diagnostics.Count - 1; index >= 0; index--)
        {
            var entry = diagnostics[index];
            var reportedIndex = entry.IndexOf(reportedFailurePrefix, StringComparison.OrdinalIgnoreCase);
            if (reportedIndex >= 0)
            {
                return entry[(reportedIndex + reportedFailurePrefix.Length)..].Trim();
            }

            var startupIndex = entry.IndexOf(startupFailurePrefix, StringComparison.OrdinalIgnoreCase);
            if (startupIndex < 0)
            {
                continue;
            }

            var colonIndex = entry.IndexOf(':', startupIndex + startupFailurePrefix.Length);
            if (colonIndex >= 0 && colonIndex + 1 < entry.Length)
            {
                return entry[(colonIndex + 1)..].Trim();
            }
        }

        return null;
    }

    private static string? NormalizeStartupFailureReason(string? failureReason)
    {
        failureReason = failureReason?.Trim();
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
