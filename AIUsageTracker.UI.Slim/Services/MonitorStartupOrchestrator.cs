// <copyright file="MonitorStartupOrchestrator.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

public sealed class MonitorStartupOrchestrator
{
    private readonly IMonitorService _monitorService;
    private readonly MonitorLifecycleService _monitorLifecycleService;
    private readonly ILogger<MonitorStartupOrchestrator> _logger;

    public MonitorStartupOrchestrator(
        IMonitorService monitorService,
        MonitorLifecycleService monitorLifecycleService,
        ILogger<MonitorStartupOrchestrator> logger)
    {
        this._monitorService = monitorService;
        this._monitorLifecycleService = monitorLifecycleService;
        this._logger = logger;
    }

    public async Task<MonitorStartupOrchestrationResult> EnsureMonitorReadyAsync(
        Func<string, StatusType, Task> reportStatusAsync,
        bool skipInitialHealthCheck = false)
    {
        ArgumentNullException.ThrowIfNull(reportStatusAsync);

        try
        {
            // If caller already checked health and it failed, skip the redundant check
            // to avoid wasting another 6+ seconds on a TCP timeout.
            var isRunning = false;
            if (!skipInitialHealthCheck)
            {
                await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
                isRunning = await this._monitorService.CheckHealthAsync().ConfigureAwait(false);
            }

            if (!isRunning)
            {
                await reportStatusAsync("Starting monitor...", StatusType.Warning).ConfigureAwait(false);

                var monitorReady = await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(false);
                if (!monitorReady)
                {
                    await this._monitorService.RefreshAgentInfoAsync().ConfigureAwait(false);
                    return new MonitorStartupOrchestrationResult(IsSuccess: false, IsLaunchFailure: true);
                }

                await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
            }
            else if (await this.TryRestartMonitorForVersionMismatchAsync(reportStatusAsync).ConfigureAwait(false))
            {
                await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
            }

            return new MonitorStartupOrchestrationResult(IsSuccess: true, IsLaunchFailure: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(ex, "Startup orchestration failed");
            return new MonitorStartupOrchestrationResult(IsSuccess: false, IsLaunchFailure: false);
        }
    }

    private async Task<bool> TryRestartMonitorForVersionMismatchAsync(Func<string, StatusType, Task> reportStatusAsync)
    {
        try
        {
            var healthSnapshot = await this._monitorService.GetHealthSnapshotAsync().ConfigureAwait(false);
            if (healthSnapshot == null)
            {
                return false;
            }

            var monitorVersion = healthSnapshot.AgentVersion;
            var uiVersion = typeof(App).Assembly.GetName().Version?.ToString();
            if (string.IsNullOrEmpty(monitorVersion) || string.IsNullOrEmpty(uiVersion))
            {
                return false;
            }

            if (string.Equals(monitorVersion, uiVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MonitorService.LogDiagnostic(
                $"Monitor version mismatch (monitor: {monitorVersion}, ui: {uiVersion}). Restarting monitor...");
            this._logger.LogWarning(
                "Monitor version mismatch (monitor: {MonitorVersion}, ui: {UiVersion}). Restarting monitor...",
                monitorVersion,
                uiVersion);

            await reportStatusAsync("Restarting monitor (version mismatch)...", StatusType.Warning).ConfigureAwait(false);

            await this._monitorLifecycleService.StopAgentAsync().ConfigureAwait(false);
            var started = await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(false);
            if (!started)
            {
                MonitorService.LogDiagnostic("Failed to restart monitor after version mismatch.");
                this._logger.LogError("Failed to restart monitor after version mismatch");
                return false;
            }

            MonitorService.LogDiagnostic("Monitor restarted successfully after version mismatch.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(ex, "Error during monitor version mismatch restart");
            return false;
        }
    }
}
