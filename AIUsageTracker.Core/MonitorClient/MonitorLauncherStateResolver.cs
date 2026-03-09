// <copyright file="MonitorLauncherStateResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient
{
    using System.Diagnostics;
    using System.Text.Json;
    using AIUsageTracker.Core.Models;

    internal static class MonitorLauncherStateResolver
    {
        internal readonly record struct MonitorMetadataState(
            MonitorInfo? Info,
            string? Path,
            bool HealthOk,
            bool ProcessRunning)
        {
            public bool IsUsable => this.Info != null && this.HealthOk && this.ProcessRunning;

            public int EffectivePort => this.Info?.Port > 0 ? this.Info.Port : MonitorLauncher.DefaultPort;
        }

        internal readonly record struct MonitorReadyState(int Port, bool IsRunning, bool FromMetadata);

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
                    if (MonitorInfoPathCatalog.IsDeprecatedReadPath(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        path))
                    {
                        MonitorService.LogDiagnostic($"Using deprecated monitor metadata path '{path}'. Rewrite will occur at the canonical AIUsageTracker path.");
                    }

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

                if (healthOk && processRunning)
                {
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

        public static async Task<MonitorLauncher.MonitorStatusInfo> GetAgentStatusInfoAsync(
            Func<Task<MonitorMetadataState>> readValidatedAgentInfoAsync,
            Func<int, Task<bool>> checkHealthAsync)
        {
            var metadataState = await readValidatedAgentInfoAsync().ConfigureAwait(false);
            if (metadataState.IsUsable)
            {
                return new MonitorLauncher.MonitorStatusInfo(
                    IsRunning: true,
                    Port: metadataState.Info!.Port,
                    HasMetadata: true,
                    Message: $"Healthy on port {metadataState.Info.Port}.",
                    Error: null);
            }

            var port = metadataState.EffectivePort;
            var isRunning = await checkHealthAsync(port).ConfigureAwait(false);
            var hasMetadata = metadataState.Path != null;
            if (isRunning)
            {
                return new MonitorLauncher.MonitorStatusInfo(
                    IsRunning: true,
                    Port: port,
                    HasMetadata: hasMetadata,
                    Message: $"Healthy on port {port}.",
                    Error: null);
            }

            if (hasMetadata)
            {
                return new MonitorLauncher.MonitorStatusInfo(
                    IsRunning: false,
                    Port: port,
                    HasMetadata: true,
                    Message: $"Monitor not reachable on port {port}.",
                    Error: "monitor-unreachable");
            }

            return new MonitorLauncher.MonitorStatusInfo(
                IsRunning: false,
                Port: port,
                HasMetadata: false,
                Message: "Monitor info file not found. Start Monitor to initialize it.",
                Error: "agent-info-missing");
        }

        public static async Task<MonitorReadyState> ResolveReadyStateAsync(
            Func<Task<MonitorMetadataState>> readValidatedAgentInfoAsync,
            Func<int, Task<bool>> checkHealthAsync)
        {
            var metadataState = await readValidatedAgentInfoAsync().ConfigureAwait(false);
            if (metadataState.IsUsable)
            {
                return new MonitorReadyState(metadataState.Info!.Port, IsRunning: true, FromMetadata: true);
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
    }
}
