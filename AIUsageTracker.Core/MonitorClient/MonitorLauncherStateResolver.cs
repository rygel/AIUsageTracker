// <copyright file="MonitorLauncherStateResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Text.Json;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

internal static class MonitorLauncherStateResolver
{
    public static async Task<(MonitorInfo? Info, string? Path)> ReadAgentInfoAsync(
        IEnumerable<string> candidatePaths,
        Func<string, Task> quarantineMonitorInfoAsync)
    {
        string? path = null;

        try
        {
            path = candidatePaths.FirstOrDefault();

            if (path != null)
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<MonitorInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

                if (info != null)
                {
                    return (info, path);
                }

                await quarantineMonitorInfoAsync(path).ConfigureAwait(false);
            }

            return (null, path);
        }
        catch (JsonException ex)
        {
            MonitorService.LogDiagnostic($"Failed to parse monitor metadata: {ex.Message}");
            if (path != null)
            {
                await quarantineMonitorInfoAsync(path).ConfigureAwait(false);
            }

            return (null, path);
        }
        catch (IOException ex)
        {
            MonitorService.LogDiagnostic($"Failed to read monitor metadata: {ex.Message}");
            return (null, path);
        }
        catch (UnauthorizedAccessException ex)
        {
            MonitorService.LogDiagnostic($"Access denied reading monitor metadata: {ex.Message}");
            return (null, path);
        }
        catch
        {
            MonitorService.LogDiagnostic("Failed to load monitor metadata for an unknown reason.");
            return (null, path);
        }
    }

    public static async Task<MonitorMetadataState> ReadValidatedAgentInfoAsync(
        Func<Task<(MonitorInfo? Info, string? Path)>> readAgentInfoAsync,
        Func<int, Task<bool>> checkHealthAsync,
        Func<int, Task<bool>> checkProcessRunningAsync,
        Func<string, Task> quarantineMonitorInfoAsync)
    {
        var (info, path) = await readAgentInfoAsync().ConfigureAwait(false);
        if (info != null)
        {
            var healthOk = await checkHealthAsync(info.Port).ConfigureAwait(false);
            var processRunning = await checkProcessRunningAsync(info.ProcessId).ConfigureAwait(false);
            var startupStatus = GetStartupStatus(info.Errors);

            if (healthOk && processRunning)
            {
                return new MonitorMetadataState(info, path, healthOk, processRunning);
            }

            if (string.Equals(startupStatus, "starting", StringComparison.OrdinalIgnoreCase) && processRunning)
            {
                MonitorService.LogDiagnostic("Monitor metadata indicates startup is still in progress.");
                return new MonitorMetadataState(info, path, healthOk, processRunning);
            }

            MonitorService.LogDiagnostic($"Monitor metadata stale: health={healthOk}, processRunning={processRunning}, invalidating metadata");
            if (path != null)
            {
                await quarantineMonitorInfoAsync(path).ConfigureAwait(false);
            }

            return new MonitorMetadataState(info, path, healthOk, processRunning);
        }

        return new MonitorMetadataState(null, path, HealthOk: false, ProcessRunning: false);
    }

    public static async Task<MonitorAgentStatus> GetAgentStatusInfoAsync(
        Func<Task<MonitorMetadataState>> readValidatedAgentInfoAsync,
        Func<int, Task<bool>> checkHealthAsync)
    {
        var metadataState = await readValidatedAgentInfoAsync().ConfigureAwait(false);
        var startupStatus = GetStartupStatus(metadataState.Info?.Errors);
        var startupFailure = GetStartupFailure(metadataState.Info?.Errors);
        if (metadataState.IsUsable)
        {
            return new MonitorAgentStatus
            {
                IsRunning = true,
                Port = metadataState.Info!.Port,
                HasMetadata = true,
                Message = $"Healthy on port {metadataState.Info.Port}.",
                Error = null,
            };
        }

        if (string.Equals(startupStatus, "starting", StringComparison.OrdinalIgnoreCase) && metadataState.ProcessRunning)
        {
            return new MonitorAgentStatus
            {
                IsRunning = false,
                Port = metadataState.EffectivePort,
                HasMetadata = true,
                Message = "Monitor is starting.",
                Error = "monitor-starting",
            };
        }

        if (!string.IsNullOrWhiteSpace(startupFailure))
        {
            return new MonitorAgentStatus
            {
                IsRunning = false,
                Port = metadataState.EffectivePort,
                HasMetadata = true,
                Message = startupFailure,
                Error = "monitor-startup-failed",
            };
        }

        var port = metadataState.EffectivePort;
        var hasMetadata = metadataState.Path != null;
        return await GetStatusFromHealthCheckAsync(port, hasMetadata, checkHealthAsync).ConfigureAwait(false);
    }

    public static async Task<MonitorReadyState> ResolveReadyStateAsync(
        Func<Task<MonitorMetadataState>> readValidatedAgentInfoAsync,
        Func<int, Task<bool>> checkHealthAsync)
    {
        var metadataState = await readValidatedAgentInfoAsync().ConfigureAwait(false);
        var startupStatus = GetStartupStatus(metadataState.Info?.Errors);
        if (metadataState.IsUsable)
        {
            return new MonitorReadyState(metadataState.Info!.Port, IsRunning: true, FromMetadata: true);
        }

        var startupFailure = GetStartupFailure(metadataState.Info?.Errors);
        if (!string.IsNullOrWhiteSpace(startupFailure))
        {
            return new MonitorReadyState(
                metadataState.EffectivePort,
                IsRunning: false,
                FromMetadata: false,
                StartupFailure: startupFailure);
        }

        if (string.Equals(startupStatus, "starting", StringComparison.OrdinalIgnoreCase) && metadataState.ProcessRunning)
        {
            return new MonitorReadyState(
                metadataState.EffectivePort,
                IsRunning: false,
                FromMetadata: false,
                IsStarting: true);
        }

        var port = metadataState.EffectivePort;
        var isRunning = await checkHealthAsync(port).ConfigureAwait(false);
        return new MonitorReadyState(port, isRunning, FromMetadata: false);
    }

    public static async Task<MonitorReadyState> GetReadyStateAsync(
        Func<Task<MonitorReadyState>> resolveReadyStateAsync)
    {
        var readyState = await resolveReadyStateAsync().ConfigureAwait(false);
        if (readyState.IsRunning)
        {
            var source = readyState.FromMetadata ? "metadata" : "health check";
            MonitorService.LogDiagnostic($"Monitor is running on port {readyState.Port} via {source}.");
            return readyState;
        }

        if (readyState.IsStarting)
        {
            MonitorService.LogDiagnostic($"Monitor startup is still in progress on port {readyState.Port}.");
            return readyState;
        }

        if (!string.IsNullOrWhiteSpace(readyState.StartupFailure))
        {
            MonitorService.LogDiagnostic($"Monitor startup reported failure: {readyState.StartupFailure}");
            return readyState;
        }

        MonitorService.LogDiagnostic($"Monitor not found on port {readyState.Port}.");
        return readyState;
    }

    public static async Task<MonitorReadyState?> WaitForReadyStateAsync(
        int maxWaitSeconds,
        Func<Task<MonitorReadyState>> resolveReadyStateAsync,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int attempt = 0;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(maxWaitSeconds))
        {
            attempt++;
            var readyState = await resolveReadyStateAsync().ConfigureAwait(false);
            if (readyState.IsRunning)
            {
                MonitorService.LogDiagnostic($"Monitor is ready on port {readyState.Port} after {stopwatch.Elapsed.TotalSeconds:F1}s.");
                return readyState;
            }

            if (!string.IsNullOrWhiteSpace(readyState.StartupFailure))
            {
                MonitorService.LogDiagnostic(
                    $"Monitor startup failed after {stopwatch.Elapsed.TotalSeconds:F1}s: {readyState.StartupFailure}");
                return null;
            }

            if (attempt % 5 == 0)
            {
                MonitorService.LogDiagnostic($"Still waiting for Monitor... (elapsed: {stopwatch.Elapsed.TotalSeconds:F1}s)");
            }

            try
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                MonitorService.LogDiagnostic($"Monitor wait cancelled after {stopwatch.Elapsed.TotalSeconds:F1}s.");
                return null;
            }
        }

        MonitorService.LogDiagnostic("Timed out waiting for Monitor.");
        return null;
    }

    private static async Task<MonitorAgentStatus> GetStatusFromHealthCheckAsync(
        int port,
        bool hasMetadata,
        Func<int, Task<bool>> checkHealthAsync)
    {
        var isRunning = await checkHealthAsync(port).ConfigureAwait(false);
        if (isRunning)
        {
            return new MonitorAgentStatus
            {
                IsRunning = true,
                Port = port,
                HasMetadata = hasMetadata,
                Message = $"Healthy on port {port}.",
                Error = null,
            };
        }

        if (hasMetadata)
        {
            return new MonitorAgentStatus
            {
                IsRunning = false,
                Port = port,
                HasMetadata = true,
                Message = $"Monitor not reachable on port {port}.",
                Error = "monitor-unreachable",
            };
        }

        return new MonitorAgentStatus
        {
            IsRunning = false,
            Port = port,
            HasMetadata = false,
            Message = "Monitor info file not found. Start Monitor to initialize it.",
            Error = "agent-info-missing",
        };
    }

    private static string? GetStartupFailure(IReadOnlyList<string>? errors)
    {
        return errors?.FirstOrDefault(error =>
            !string.IsNullOrWhiteSpace(error) &&
            error.StartsWith("Startup status:", StringComparison.OrdinalIgnoreCase) &&
            !error.Contains("running", StringComparison.OrdinalIgnoreCase) &&
            !error.Contains("starting", StringComparison.OrdinalIgnoreCase) &&
            !error.Contains("stopped", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetStartupStatus(IReadOnlyList<string>? errors)
    {
        var startupEntry = errors?.FirstOrDefault(error =>
            !string.IsNullOrWhiteSpace(error) &&
            error.StartsWith("Startup status:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(startupEntry))
        {
            return null;
        }

        return startupEntry["Startup status:".Length..].Trim();
    }

    internal readonly record struct MonitorMetadataState(
        MonitorInfo? Info,
        string? Path,
        bool HealthOk,
        bool ProcessRunning)
    {
        public bool IsUsable => this.Info != null && this.HealthOk && this.ProcessRunning;

        public int EffectivePort => this.Info?.Port > 0 ? this.Info.Port : MonitorLauncher.DefaultPort;
    }

    internal readonly record struct MonitorReadyState(
        int Port,
        bool IsRunning,
        bool FromMetadata,
        string? StartupFailure = null,
        bool IsStarting = false);
}
