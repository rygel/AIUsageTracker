// <copyright file="MonitorLauncher.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient
{
    using System.Diagnostics;
    using System.Net.Http;
    using AIUsageTracker.Core.Models;
    using Microsoft.Extensions.Logging;

    public class MonitorLauncher
    {
        internal const int DefaultPort = 5000;
        private const int MaxWaitSeconds = 30;
        private const int StopWaitSeconds = 5;
        private static ILogger<MonitorLauncher>? _logger;
        private static readonly SemaphoreSlim StartupSemaphore = new(1, 1);
        private static Func<IEnumerable<string>>? _monitorInfoCandidatePathsOverride;
        private static Func<int, Task<bool>>? _healthCheckOverride;
        private static Func<int, Task<bool>>? _processRunningOverride;
        private static Func<int, Task<bool>>? _stopProcessOverride;
        private static Func<Task<bool>>? _stopNamedProcessesOverride;

        public readonly record struct MonitorStatusInfo(
            bool IsRunning,
            int Port,
            bool HasMetadata,
            string Message,
            string? Error);

        public static void SetLogger(ILogger<MonitorLauncher> logger) => _logger = logger;

        internal static IDisposable PushTestOverrides(
            IEnumerable<string>? monitorInfoCandidatePaths = null,
            Func<int, Task<bool>>? healthCheckAsync = null,
            Func<int, Task<bool>>? processRunningAsync = null,
            Func<int, Task<bool>>? stopProcessAsync = null,
            Func<Task<bool>>? stopNamedProcessesAsync = null)
        {
            var previousCandidatePaths = _monitorInfoCandidatePathsOverride;
            var previousHealthCheck = _healthCheckOverride;
            var previousProcessCheck = _processRunningOverride;
            var previousStopProcess = _stopProcessOverride;
            var previousStopNamedProcesses = _stopNamedProcessesOverride;

            if (monitorInfoCandidatePaths != null)
            {
                var paths = monitorInfoCandidatePaths.ToArray();
                _monitorInfoCandidatePathsOverride = () => paths;
            }

            if (healthCheckAsync != null)
            {
                _healthCheckOverride = healthCheckAsync;
            }

            if (processRunningAsync != null)
            {
                _processRunningOverride = processRunningAsync;
            }

            if (stopProcessAsync != null)
            {
                _stopProcessOverride = stopProcessAsync;
            }

            if (stopNamedProcessesAsync != null)
            {
                _stopNamedProcessesOverride = stopNamedProcessesAsync;
            }

            return new TestOverrideScope(() =>
            {
                _monitorInfoCandidatePathsOverride = previousCandidatePaths;
                _healthCheckOverride = previousHealthCheck;
                _processRunningOverride = previousProcessCheck;
                _stopProcessOverride = previousStopProcess;
                _stopNamedProcessesOverride = previousStopNamedProcesses;
            });
        }

        public static async Task<int> GetAgentPortAsync()
        {
            var readyState = await ResolveReadyStateAsync().ConfigureAwait(false);
            return readyState.Port;
        }

        public static async Task<bool> IsAgentRunningAsync()
        {
            var readyState = await GetReadyStateAsync().ConfigureAwait(false);
            return readyState.IsRunning;
        }

        public static async Task<(bool IsRunning, int Port)> IsAgentRunningWithPortAsync()
        {
            var readyState = await GetReadyStateAsync().ConfigureAwait(false);
            return (readyState.IsRunning, readyState.Port);
        }

        public static async Task<(bool IsRunning, int Port, bool HasMetadata)> GetAgentStatusAsync()
        {
            var status = await GetAgentStatusInfoAsync().ConfigureAwait(false);
            return (status.IsRunning, status.Port, status.HasMetadata);
        }

        public static async Task<MonitorStatusInfo> GetAgentStatusInfoAsync()
        {
            return await MonitorLauncherStateResolver.GetAgentStatusInfoAsync(
                ReadValidatedAgentInfoAsync,
                CheckHealthAsync).ConfigureAwait(false);
        }

        private static async Task<bool> CheckHealthAsync(int port)
        {
            if (_healthCheckOverride != null)
            {
                return await _healthCheckOverride(port).ConfigureAwait(false);
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                var response = await client.GetAsync($"http://localhost:{port}/api/health").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex)
            {
                MonitorService.LogDiagnostic($"Health check request failed on port {port}: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                MonitorService.LogDiagnostic($"Health check timed out on port {port}: {ex.Message}");
                return false;
            }
            catch
            {
                MonitorService.LogDiagnostic($"Health check failed on port {port} for an unknown reason.");
                return false;
            }
        }

        private static Task<bool> CheckProcessRunningAsync(int processId)
        {
            if (_processRunningOverride != null)
            {
                return _processRunningOverride(processId);
            }

            if (processId <= 0)
            {
                return Task.FromResult(false);
            }

            try
            {
                var process = Process.GetProcessById(processId);
                return Task.FromResult(!process.HasExited);
            }
            catch (ArgumentException)
            {
                MonitorService.LogDiagnostic($"Monitor process {processId} was not found.");
                return Task.FromResult(false);
            }
            catch
            {
                MonitorService.LogDiagnostic($"Failed to query monitor process {processId}.");
                return Task.FromResult(false);
            }
        }

        public static async Task<MonitorInfo?> GetAndValidateMonitorInfoAsync()
        {
            var metadataState = await ReadValidatedAgentInfoAsync().ConfigureAwait(false);
            return metadataState.IsUsable ? metadataState.Info : null;
        }

        public static Task InvalidateMonitorInfoAsync()
        {
            try
            {
                foreach (var infoPath in GetExistingAgentInfoPaths())
                {
                    InvalidateMonitorInfoPath(infoPath);
                }
            }
            catch (Exception ex)
            {
                MonitorService.LogDiagnostic($"Failed to invalidate monitor metadata: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private static Task QuarantineMonitorInfoAsync(string infoPath, string? diagnosticMessage = null)
        {
            if (!string.IsNullOrEmpty(diagnosticMessage))
            {
                MonitorService.LogDiagnostic(diagnosticMessage);
            }

            InvalidateMonitorInfoPath(infoPath);
            return Task.CompletedTask;
        }

        private static void InvalidateMonitorInfoPath(string infoPath)
        {
            var backupPath = infoPath + ".stale." + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.Move(infoPath, backupPath, overwrite: true);
            MonitorService.LogDiagnostic($"Backed up stale metadata to: {backupPath}");
        }

        public static async Task<bool> StartAgentAsync()
        {
            await StartupSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var readyState = await ResolveReadyStateAsync().ConfigureAwait(false);
                if (readyState.IsRunning)
                {
                    var source = readyState.FromMetadata ? "metadata" : "health check";
                    MonitorService.LogDiagnostic($"Monitor already running on port {readyState.Port} via {source}; skipping start.");
                    return true;
                }

                var launchPlan = MonitorLauncherProcessController.TryResolveLaunchPlan(readyState.Port);
                if (launchPlan == null)
                {
                    return false;
                }

                return MonitorLauncherProcessController.TryStartMonitorProcess(
                    launchPlan.Value.StartInfo,
                    launchPlan.Value.LaunchTarget);
            }
            catch (Exception ex)
            {
                MonitorService.LogDiagnostic($"Failed to start Monitor: {ex.Message}");
                return false;
            }
            finally
            {
                StartupSemaphore.Release();
            }
        }

        public static async Task<bool> EnsureAgentRunningAsync(CancellationToken cancellationToken = default)
        {
            var readyState = await GetReadyStateAsync().ConfigureAwait(false);
            if (readyState.IsRunning)
            {
                MonitorService.LogDiagnostic($"Monitor already ready on port {readyState.Port}; no startup needed.");
                return true;
            }

            var started = await StartAgentAsync().ConfigureAwait(false);
            if (!started)
            {
                return false;
            }

            var waitedState = await WaitForReadyStateAsync(cancellationToken).ConfigureAwait(false);
            return waitedState.HasValue;
        }

        public static async Task<bool> StopAgentAsync()
        {
            var metadataState = await ReadValidatedAgentInfoAsync().ConfigureAwait(false);
            return await MonitorLauncherProcessController.StopAgentAsync(
                metadataState.Info,
                metadataState.EffectivePort,
                StopWaitSeconds,
                CheckHealthAsync,
                processId => MonitorLauncherProcessController.TryStopProcessAsync(processId, StopWaitSeconds, _stopProcessOverride),
                () => MonitorLauncherProcessController.TryStopNamedProcessesAsync(StopWaitSeconds, _stopNamedProcessesOverride),
                InvalidateMonitorInfoAsync,
                _logger).ConfigureAwait(false);
        }

        public static async Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default)
        {
            MonitorService.LogDiagnostic($"Waiting for Monitor to start (max {MaxWaitSeconds}s)...");
            var readyState = await WaitForReadyStateAsync(cancellationToken).ConfigureAwait(false);
            return readyState.HasValue;
        }

        private static async Task<MonitorLauncherStateResolver.MonitorReadyState?> WaitForReadyStateAsync(CancellationToken cancellationToken)
        {
            return await MonitorLauncherStateResolver.WaitForReadyStateAsync(
                MaxWaitSeconds,
                ResolveReadyStateAsync,
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<MonitorLauncherStateResolver.MonitorReadyState> ResolveReadyStateAsync()
        {
            return await MonitorLauncherStateResolver.ResolveReadyStateAsync(
                ReadValidatedAgentInfoAsync,
                CheckHealthAsync).ConfigureAwait(false);
        }

        private static async Task<MonitorLauncherStateResolver.MonitorReadyState> GetReadyStateAsync()
        {
            return await MonitorLauncherStateResolver.GetReadyStateAsync(ResolveReadyStateAsync).ConfigureAwait(false);
        }

        private static Task<(MonitorInfo? Info, string? Path)> ReadAgentInfoAsync()
        {
            return MonitorLauncherStateResolver.ReadAgentInfoAsync(
                GetExistingAgentInfoPaths(),
                infoPath => QuarantineMonitorInfoAsync(
                    infoPath,
                    $"Monitor metadata at '{infoPath}' was empty or invalid; invalidating metadata."));
        }

        private static Task<MonitorLauncherStateResolver.MonitorMetadataState> ReadValidatedAgentInfoAsync()
        {
            return MonitorLauncherStateResolver.ReadValidatedAgentInfoAsync(
                ReadAgentInfoAsync,
                CheckHealthAsync,
                CheckProcessRunningAsync,
                infoPath => QuarantineMonitorInfoAsync(infoPath));
        }

        private static string? GetExistingAgentInfoPath()
        {
            return GetExistingAgentInfoPaths().FirstOrDefault();
        }

        private static IEnumerable<string> GetExistingAgentInfoPaths()
        {
            return GetMonitorInfoCandidatePaths()
                .Where(File.Exists)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path));
        }

        private static IEnumerable<string> GetMonitorInfoCandidatePaths()
        {
            if (_monitorInfoCandidatePathsOverride != null)
            {
                return _monitorInfoCandidatePathsOverride();
            }

            return MonitorInfoPathCatalog.GetReadCandidatePathsFromEnvironment();
        }

        private sealed class TestOverrideScope : IDisposable
        {
            private readonly Action _reset;
            private bool _disposed;

            public TestOverrideScope(Action reset)
            {
                this._reset = reset;
            }

            public void Dispose()
            {
                if (this._disposed)
                {
                    return;
                }

                this._disposed = true;
                this._reset();
            }
        }
    }
}
