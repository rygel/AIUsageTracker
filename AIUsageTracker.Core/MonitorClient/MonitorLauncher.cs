// <copyright file="MonitorLauncher.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
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
    private const string CanonicalProductFolder = "AIUsageTracker";

    private static readonly char[] DirectorySeparators = ['\\', '/'];
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

    public static IReadOnlyList<string> GetMonitorInfoWriteCandidatePaths(string appDataRoot, string userProfileRoot)
    {
        _ = userProfileRoot;
        return new[] { GetCanonicalMonitorInfoPath(appDataRoot) };
    }

    public static IReadOnlyList<string> GetMonitorInfoReadCandidatePaths(string appDataRoot, string userProfileRoot)
    {
        _ = userProfileRoot;
        return new[] { GetCanonicalMonitorInfoPath(appDataRoot) };
    }

    public static IReadOnlyList<string> GetMonitorInfoReadCandidatePathsFromEnvironment()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return GetMonitorInfoReadCandidatePaths(appDataRoot, userProfileRoot);
    }

    public static string? ResolveExistingMonitorInfoReadPath()
    {
        return GetMonitorInfoReadCandidatePathsFromEnvironment()
            .Where(File.Exists)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
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
        var metadataState = await ReadValidatedAgentInfoAsync().ConfigureAwait(false);
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
        var isRunning = await CheckHealthAsync(port).ConfigureAwait(false);
        if (isRunning)
        {
            return new MonitorAgentStatus
            {
                IsRunning = true, Port = port, HasMetadata = hasMetadata,
                Message = $"Healthy on port {port}.", Error = null,
            };
        }

        return new MonitorAgentStatus
        {
            IsRunning = false, Port = port, HasMetadata = hasMetadata,
            Message = hasMetadata
                ? $"Monitor not reachable on port {port}."
                : "Monitor info file not found. Start Monitor to initialize it.",
            Error = hasMetadata ? "monitor-unreachable" : "agent-info-missing",
        };
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

    // --- State resolution (inlined from former MonitorLauncherStateResolver) ---

    private static async Task<MonitorReadyState> ResolveReadyStateAsync()
    {
        var metadataState = await ReadValidatedAgentInfoAsync().ConfigureAwait(false);
        var startupStatus = GetStartupStatus(metadataState.Info?.Errors);

        if (metadataState.IsUsable)
        {
            return new MonitorReadyState(metadataState.Info!.Port, IsRunning: true, FromMetadata: true);
        }

        var startupFailure = GetStartupFailure(metadataState.Info?.Errors);
        if (!string.IsNullOrWhiteSpace(startupFailure))
        {
            return new MonitorReadyState(metadataState.EffectivePort, IsRunning: false, FromMetadata: false, StartupFailure: startupFailure);
        }

        if (string.Equals(startupStatus, "starting", StringComparison.OrdinalIgnoreCase) && metadataState.ProcessRunning)
        {
            return new MonitorReadyState(metadataState.EffectivePort, IsRunning: false, FromMetadata: false, IsStarting: true);
        }

        var port = metadataState.EffectivePort;
        var isRunning = await CheckHealthAsync(port).ConfigureAwait(false);
        return new MonitorReadyState(port, isRunning, FromMetadata: false);
    }

    private static async Task<MonitorReadyState> GetReadyStateAsync()
    {
        var readyState = await ResolveReadyStateAsync().ConfigureAwait(false);
        if (readyState.IsRunning)
        {
            var source = readyState.FromMetadata ? "metadata" : "health check";
            MonitorService.LogDiagnostic($"Monitor is running on port {readyState.Port} via {source}.");
        }
        else if (readyState.IsStarting)
        {
            MonitorService.LogDiagnostic($"Monitor startup is still in progress on port {readyState.Port}.");
        }
        else if (!string.IsNullOrWhiteSpace(readyState.StartupFailure))
        {
            MonitorService.LogDiagnostic($"Monitor startup reported failure: {readyState.StartupFailure}");
        }
        else
        {
            MonitorService.LogDiagnostic($"Monitor not found on port {readyState.Port}.");
        }

        return readyState;
    }

    private static async Task<MonitorReadyState?> WaitForReadyStateAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int attempt = 0;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(MaxWaitSeconds))
        {
            attempt++;
            var readyState = await ResolveReadyStateAsync().ConfigureAwait(false);
            if (readyState.IsRunning)
            {
                MonitorService.LogDiagnostic($"Monitor is ready on port {readyState.Port} after {stopwatch.Elapsed.TotalSeconds:F1}s.");
                return readyState;
            }

            if (!string.IsNullOrWhiteSpace(readyState.StartupFailure))
            {
                MonitorService.LogDiagnostic($"Monitor startup failed after {stopwatch.Elapsed.TotalSeconds:F1}s: {readyState.StartupFailure}");
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

    private static async Task<(MonitorInfo? Info, string? Path)> ReadAgentInfoAsync()
    {
        string? path = null;
        try
        {
            path = GetExistingAgentInfoPaths().FirstOrDefault();
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

                QuarantineMonitorInfoPath(path, "Monitor metadata was empty or invalid; invalidating.");
            }

            return (null, path);
        }
        catch (JsonException ex)
        {
            MonitorService.LogDiagnostic($"Failed to parse monitor metadata: {ex.Message}");
            if (path != null)
            {
                QuarantineMonitorInfoPath(path);
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

    private static async Task<MonitorMetadataState> ReadValidatedAgentInfoAsync()
    {
        var (info, path) = await ReadAgentInfoAsync().ConfigureAwait(false);
        if (info == null)
        {
            return new MonitorMetadataState(null, path, HealthOk: false, ProcessRunning: false);
        }

        var healthOk = await CheckHealthAsync(info.Port).ConfigureAwait(false);
        var processRunning = await CheckProcessRunningAsync(info.ProcessId).ConfigureAwait(false);
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
            QuarantineMonitorInfoPath(path);
        }

        return new MonitorMetadataState(info, path, healthOk, processRunning);
    }

    // --- Infrastructure methods ---

    private static void QuarantineMonitorInfoPath(string infoPath, string? diagnosticMessage = null)
    {
        if (!string.IsNullOrEmpty(diagnosticMessage))
        {
            MonitorService.LogDiagnostic(diagnosticMessage);
        }

        InvalidateMonitorInfoPath(infoPath);
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

        try
        {
            var staleFiles = Directory.GetFiles(directory, fileName + ".stale.*", SearchOption.TopDirectoryOnly)
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

        return GetMonitorInfoReadCandidatePathsFromEnvironment();
    }

    private static string GetCanonicalMonitorInfoPath(string appDataRoot)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            return Path.Combine(CanonicalProductFolder, "monitor.json");
        }

        var normalizedRoot = appDataRoot.TrimEnd(DirectorySeparators);
        var leaf = Path.GetFileName(normalizedRoot);
        if (leaf.Equals(CanonicalProductFolder, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(normalizedRoot, "monitor.json");
        }

        return Path.Combine(normalizedRoot, CanonicalProductFolder, "monitor.json");
    }

    private static string BuildLaunchMutexName()
    {
        return MutexNameBuilder.BuildGlobalName("AIUsageTracker_MonitorLaunch_");
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

    // --- Types ---

    internal readonly record struct MonitorMetadataState(
        MonitorInfo? Info,
        string? Path,
        bool HealthOk,
        bool ProcessRunning)
    {
        public bool IsUsable => this.Info != null && this.HealthOk && this.ProcessRunning;
        public int EffectivePort => this.Info?.Port > 0 ? this.Info.Port : DefaultPort;
    }

    internal readonly record struct MonitorReadyState(
        int Port,
        bool IsRunning,
        bool FromMetadata,
        string? StartupFailure = null,
        bool IsStarting = false);

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
