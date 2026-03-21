// <copyright file="MonitorLauncher.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Runtime;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Core.MonitorClient;

public class MonitorLauncher
{
    internal const int DefaultPort = 5000;
    internal const int MaxStaleMetadataBackups = 10;
    private const int MaxWaitSeconds = 30;
    private const int StopWaitSeconds = 5;
    private const int LaunchMutexWaitSeconds = 3;

    private static readonly SemaphoreSlim StartupSemaphore = new(1, 1);
    private static readonly HttpClient HealthCheckHttpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
    private static readonly AsyncLocal<TestOverrides?> TestOverridesContext = new();
    private static ILogger<MonitorLauncher>? _logger;

    public static void SetLogger(ILogger<MonitorLauncher> logger) => _logger = logger;

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

    public static async Task<MonitorAgentStatus> GetAgentStatusInfoAsync()
    {
        return await MonitorLauncherStateResolver.GetAgentStatusInfoAsync(
            ReadValidatedAgentInfoAsync,
            CheckHealthAsync).ConfigureAwait(false);
    }

    public static async Task<MonitorInfo?> GetAndValidateMonitorInfoAsync()
    {
        var metadataState = await ReadValidatedAgentInfoAsync().ConfigureAwait(false);
        return metadataState.IsUsable ? metadataState.Info : null;
    }

    public static async Task<bool> StartAgentAsync()
    {
        await StartupSemaphore.WaitAsync().ConfigureAwait(false);
        Mutex? launchMutex = null;
        var holdsLaunchMutex = false;
        try
        {
            launchMutex = new Mutex(initiallyOwned: false, name: BuildLaunchMutexName());
            try
            {
                holdsLaunchMutex = launchMutex.WaitOne(TimeSpan.FromSeconds(LaunchMutexWaitSeconds));
            }
            catch (AbandonedMutexException)
            {
                holdsLaunchMutex = true;
                MonitorService.LogDiagnostic("Monitor launch lock was abandoned; proceeding.");
            }

            if (!holdsLaunchMutex)
            {
                MonitorService.LogDiagnostic("Monitor launch lock is held by another process; waiting for readiness.");
                var waitedState = await WaitForReadyStateAsync(CancellationToken.None).ConfigureAwait(false);
                return waitedState.HasValue;
            }

            var readyState = await ResolveReadyStateAsync().ConfigureAwait(false);
            if (readyState.IsRunning)
            {
                var source = readyState.FromMetadata ? "metadata" : "health check";
                MonitorService.LogDiagnostic($"Monitor already running on port {readyState.Port} via {source}; skipping start.");
                return true;
            }

            if (readyState.IsStarting)
            {
                MonitorService.LogDiagnostic($"Monitor startup already in progress on port {readyState.Port}; skipping duplicate launch.");
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
            if (holdsLaunchMutex && launchMutex != null)
            {
                try
                {
                    launchMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ignore release attempts when lock ownership was lost unexpectedly.
                }
            }

            launchMutex?.Dispose();
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

        if (readyState.IsStarting)
        {
            MonitorService.LogDiagnostic($"Monitor startup already in progress on port {readyState.Port}; waiting for readiness.");
            var startingState = await WaitForReadyStateAsync(cancellationToken).ConfigureAwait(false);
            return startingState.HasValue;
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
        var testOverrides = TestOverridesContext.Value;
        return await MonitorLauncherProcessController.StopAgentAsync(
            metadataState.Info,
            metadataState.EffectivePort,
            StopWaitSeconds,
            CheckHealthAsync,
            processId => MonitorLauncherProcessController.TryStopProcessAsync(
                processId,
                StopWaitSeconds,
                testOverrides?.StopProcessOverride),
            () => MonitorLauncherProcessController.TryStopNamedProcessesAsync(
                StopWaitSeconds,
                testOverrides?.StopNamedProcessesOverride),
            InvalidateMonitorInfoAsync,
            _logger).ConfigureAwait(false);
    }

    public static async Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default)
    {
        MonitorService.LogDiagnostic($"Waiting for Monitor to start (max {MaxWaitSeconds}s)...");
        var readyState = await WaitForReadyStateAsync(cancellationToken).ConfigureAwait(false);
        return readyState.HasValue;
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

    internal static async Task<MonitorMetadataStatus> GetMonitorMetadataSnapshotAsync()
    {
        var metadataState = await ReadValidatedAgentInfoAsync().ConfigureAwait(false);
        return new MonitorMetadataStatus
        {
            Info = metadataState.Info,
            IsUsable = metadataState.IsUsable,
        };
    }

    internal static IDisposable PushTestOverrides(
        IEnumerable<string>? monitorInfoCandidatePaths = null,
        Func<int, Task<bool>>? healthCheckAsync = null,
        Func<int, Task<bool>>? processRunningAsync = null,
        Func<int, Task<bool>>? stopProcessAsync = null,
        Func<Task<bool>>? stopNamedProcessesAsync = null)
    {
        var previousOverrides = TestOverridesContext.Value;
        var nextOverrides = previousOverrides?.Clone() ?? new TestOverrides();

        if (monitorInfoCandidatePaths != null)
        {
            var paths = monitorInfoCandidatePaths.ToArray();
            nextOverrides.MonitorInfoCandidatePathsOverride = () => paths;
        }

        if (healthCheckAsync != null)
        {
            nextOverrides.HealthCheckOverride = healthCheckAsync;
        }

        if (processRunningAsync != null)
        {
            nextOverrides.ProcessRunningOverride = processRunningAsync;
        }

        if (stopProcessAsync != null)
        {
            nextOverrides.StopProcessOverride = stopProcessAsync;
        }

        if (stopNamedProcessesAsync != null)
        {
            nextOverrides.StopNamedProcessesOverride = stopNamedProcessesAsync;
        }

        TestOverridesContext.Value = nextOverrides;

        return new TestOverrideScope(() =>
        {
            TestOverridesContext.Value = previousOverrides;
        });
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
        CleanupOldStaleMetadataBackups(infoPath);
    }

    private static void CleanupOldStaleMetadataBackups(string infoPath)
    {
        var directory = Path.GetDirectoryName(infoPath);
        var fileName = Path.GetFileName(infoPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var pattern = fileName + ".stale.*";
        try
        {
            var staleFiles = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .Skip(MaxStaleMetadataBackups)
                .ToList();

            foreach (var staleFile in staleFiles)
            {
                File.Delete(staleFile);
            }
        }
        catch (Exception ex)
        {
            MonitorService.LogDiagnostic($"Failed pruning stale monitor metadata backups: {ex.Message}");
        }
    }

    private static async Task<bool> CheckHealthAsync(int port)
    {
        var testOverrides = TestOverridesContext.Value;
        if (testOverrides?.HealthCheckOverride != null)
        {
            return await testOverrides.HealthCheckOverride(port).ConfigureAwait(false);
        }

        try
        {
            var response = await HealthCheckHttpClient.GetAsync($"http://localhost:{port}/api/health").ConfigureAwait(false);
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
        var testOverrides = TestOverridesContext.Value;
        if (testOverrides?.ProcessRunningOverride != null)
        {
            return testOverrides.ProcessRunningOverride(processId);
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
        var testOverrides = TestOverridesContext.Value;
        if (testOverrides?.MonitorInfoCandidatePathsOverride != null)
        {
            return testOverrides.MonitorInfoCandidatePathsOverride();
        }

        return MonitorInfoPathCatalog.GetReadCandidatePathsFromEnvironment();
    }

    private static string BuildLaunchMutexName()
    {
        return MutexNameBuilder.BuildGlobalName("AIUsageTracker_MonitorLaunch_");
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

    private sealed class TestOverrides
    {
        public Func<IEnumerable<string>>? MonitorInfoCandidatePathsOverride { get; set; }

        public Func<int, Task<bool>>? HealthCheckOverride { get; set; }

        public Func<int, Task<bool>>? ProcessRunningOverride { get; set; }

        public Func<int, Task<bool>>? StopProcessOverride { get; set; }

        public Func<Task<bool>>? StopNamedProcessesOverride { get; set; }

        public TestOverrides Clone()
        {
            return new TestOverrides
            {
                MonitorInfoCandidatePathsOverride = this.MonitorInfoCandidatePathsOverride,
                HealthCheckOverride = this.HealthCheckOverride,
                ProcessRunningOverride = this.ProcessRunningOverride,
                StopProcessOverride = this.StopProcessOverride,
                StopNamedProcessesOverride = this.StopNamedProcessesOverride,
            };
        }
    }
}
