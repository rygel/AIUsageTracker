// <copyright file="MonitorLauncher.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Runtime;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Core.MonitorClient;

public class MonitorLauncher : IMonitorLauncher
{
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };
    internal const int DefaultPort = 5000;
    internal const int MaxStaleMetadataBackups = 10;
    private const int MaxWaitSeconds = 30;
    private const int StopWaitSeconds = 5;
    private const int LaunchMutexWaitSeconds = 3;
    private const string CanonicalProductFolder = "AIUsageTracker";
    private const string StatusStarting = "starting";
    private const string StartupStatusPrefix = "Startup status:";

    private readonly SemaphoreSlim _startupSemaphore = new(1, 1);
    private readonly HttpClient _healthCheckHttpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
    private readonly ILogger<MonitorLauncher>? _logger;
    private readonly string? _monitorInfoPathOverride;
    private readonly Func<int, Task<bool>>? _healthCheckOverride;
    private readonly Func<int, Task<bool>>? _processRunningOverride;
    private readonly Func<int, Task<bool>>? _stopProcessOverride;
    private readonly Func<Task<bool>>? _stopNamedProcessesOverride;

    /// <summary>
    /// DI constructor — takes only the logger. Used by the DI container.
    /// </summary>
    public MonitorLauncher(ILogger<MonitorLauncher> logger)
        : this(logger, monitorInfoCandidatePathsOverride: null)
    {
    }

    /// <summary>
    /// Test constructor — accepts optional overrides for health check, process check, etc.
    /// </summary>
    internal MonitorLauncher(
        ILogger<MonitorLauncher>? logger = null,
        Func<IEnumerable<string>>? monitorInfoCandidatePathsOverride = null,
        Func<int, Task<bool>>? healthCheckOverride = null,
        Func<int, Task<bool>>? processRunningOverride = null,
        Func<int, Task<bool>>? stopProcessOverride = null,
        Func<Task<bool>>? stopNamedProcessesOverride = null)
    {
        this._logger = logger;
        this._monitorInfoPathOverride = monitorInfoCandidatePathsOverride?.Invoke().FirstOrDefault();
        this._healthCheckOverride = healthCheckOverride;
        this._processRunningOverride = processRunningOverride;
        this._stopProcessOverride = stopProcessOverride;
        this._stopNamedProcessesOverride = stopNamedProcessesOverride;
    }

    public async Task<int> GetAgentPortAsync()
    {
        var readyState = await this.ResolveReadyStateAsync().ConfigureAwait(false);
        return readyState.Port;
    }

    public async Task<bool> IsAgentRunningAsync()
    {
        var readyState = await this.GetReadyStateAsync().ConfigureAwait(false);
        return readyState.IsRunning;
    }

    public static string GetCanonicalMonitorInfoFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Join(localAppData, CanonicalProductFolder, "monitor.json");
    }

    public async Task<(bool IsRunning, int Port)> IsAgentRunningWithPortAsync()
    {
        var readyState = await this.GetReadyStateAsync().ConfigureAwait(false);
        return (readyState.IsRunning, readyState.Port);
    }

    public async Task<(bool IsRunning, int Port, bool HasMetadata)> GetAgentStatusAsync()
    {
        var status = await this.GetAgentStatusInfoAsync().ConfigureAwait(false);
        return (status.IsRunning, status.Port, status.HasMetadata);
    }

    public async Task<MonitorAgentStatus> GetAgentStatusInfoAsync()
    {
        var metadataState = await this.ReadValidatedAgentInfoAsync().ConfigureAwait(false);
        var startupStatus = GetStartupStatus(metadataState.Info?.Errors);
        var startupFailure = GetStartupFailure(metadataState.Info?.Errors);

        if (metadataState.IsUsable)
        {
            return new MonitorAgentStatus
            {
                IsRunning = true,
                Port = metadataState.Info!.Port,
                HasMetadata = true,
                Message = $"Healthy on port {metadataState.Info.Port.ToString(CultureInfo.InvariantCulture)}.",
                Error = null,
            };
        }

        if (string.Equals(startupStatus, StatusStarting, StringComparison.OrdinalIgnoreCase) && metadataState.ProcessRunning)
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
        var isRunning = await this.CheckHealthAsync(port).ConfigureAwait(false);
        if (isRunning)
        {
            return new MonitorAgentStatus
            {
                IsRunning = true,
                Port = port,
                HasMetadata = hasMetadata,
                Message = $"Healthy on port {port.ToString(CultureInfo.InvariantCulture)}.",
                Error = null,
            };
        }

        return new MonitorAgentStatus
        {
            IsRunning = false,
            Port = port,
            HasMetadata = hasMetadata,
            Message = hasMetadata
                ? $"Monitor not reachable on port {port.ToString(CultureInfo.InvariantCulture)}."
                : "Monitor info file not found. Start Monitor to initialize it.",
            Error = hasMetadata ? "monitor-unreachable" : "agent-info-missing",
        };
    }

    public async Task<MonitorInfo?> GetAndValidateMonitorInfoAsync()
    {
        var metadataState = await this.ReadValidatedAgentInfoAsync().ConfigureAwait(false);
        return metadataState.IsUsable ? metadataState.Info : null;
    }

    public async Task<bool> StartAgentAsync()
    {
        await this._startupSemaphore.WaitAsync().ConfigureAwait(false);
        using var launchMutex = new Mutex(initiallyOwned: false, name: BuildLaunchMutexName());
        var holdsLaunchMutex = false;
        try
        {
            try
            {
                holdsLaunchMutex = launchMutex.WaitOne(TimeSpan.FromSeconds(LaunchMutexWaitSeconds));
            }
            catch (AbandonedMutexException ex)
            {
                holdsLaunchMutex = true;
                this._logger?.LogDebug(ex, "Monitor launch lock was abandoned; proceeding.");
            }

            if (!holdsLaunchMutex)
            {
                this._logger?.LogDebug("Monitor launch lock is held by another process; waiting for readiness.");
                var waitedState = await this.WaitForReadyStateAsync(CancellationToken.None).ConfigureAwait(false);
                return waitedState.HasValue;
            }

            var readyState = await this.ResolveReadyStateAsync().ConfigureAwait(false);
            if (readyState.IsRunning)
            {
                var source = readyState.FromMetadata ? "metadata" : "health check";
                this._logger?.LogDebug("Monitor already running on port {Port} via {Source}; skipping start.", readyState.Port, source);
                return true;
            }

            if (readyState.IsStarting)
            {
                this._logger?.LogDebug("Monitor startup already in progress on port {Port}; skipping duplicate launch.", readyState.Port);
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
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            this._logger?.LogDebug(ex, "Failed to start Monitor: {Message}", ex.Message);
            return false;
        }
        finally
        {
            if (holdsLaunchMutex)
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

            this._startupSemaphore.Release();
        }
    }

    public async Task<bool> EnsureAgentRunningAsync(CancellationToken cancellationToken = default)
    {
        var readyState = await this.GetReadyStateAsync().ConfigureAwait(false);
        if (readyState.IsRunning)
        {
            this._logger?.LogDebug("Monitor already ready on port {Port}; no startup needed.", readyState.Port);
            return true;
        }

        if (readyState.IsStarting)
        {
            this._logger?.LogDebug("Monitor startup already in progress on port {Port}; waiting for readiness.", readyState.Port);
            var startingState = await this.WaitForReadyStateAsync(cancellationToken).ConfigureAwait(false);
            return startingState.HasValue;
        }

        var started = await this.StartAgentAsync().ConfigureAwait(false);
        if (!started)
        {
            return false;
        }

        var waitedState = await this.WaitForReadyStateAsync(cancellationToken).ConfigureAwait(false);
        return waitedState.HasValue;
    }

    public async Task<bool> StopAgentAsync()
    {
        var metadataState = await this.ReadValidatedAgentInfoAsync().ConfigureAwait(false);
        return await MonitorLauncherProcessController.StopAgentAsync(
            metadataState.Info,
            metadataState.EffectivePort,
            StopWaitSeconds,
            this.CheckHealthAsync,
            processId => MonitorLauncherProcessController.TryStopProcessAsync(
                processId,
                StopWaitSeconds,
                this._stopProcessOverride),
            () => MonitorLauncherProcessController.TryStopNamedProcessesAsync(
                StopWaitSeconds,
                this._stopNamedProcessesOverride),
            this.InvalidateMonitorInfoAsync,
            this._logger).ConfigureAwait(false);
    }

    public async Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default)
    {
        this._logger?.LogDebug("Waiting for Monitor to start (max {MaxWaitSeconds}s)...", MaxWaitSeconds);
        var readyState = await this.WaitForReadyStateAsync(cancellationToken).ConfigureAwait(false);
        return readyState.HasValue;
    }

    public Task InvalidateMonitorInfoAsync()
    {
        try
        {
            var infoPath = this.GetMonitorInfoPath();
            if (File.Exists(infoPath))
            {
                this.InvalidateMonitorInfoPath(infoPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this._logger?.LogDebug(ex, "Failed to invalidate monitor metadata: {Message}", ex.Message);
        }

        return Task.CompletedTask;
    }

    public async Task<MonitorMetadataStatus> GetMonitorMetadataSnapshotAsync()
    {
        var metadataState = await this.ReadValidatedAgentInfoAsync().ConfigureAwait(false);
        return new MonitorMetadataStatus
        {
            Info = metadataState.Info,
            IsUsable = metadataState.IsUsable,
        };
    }

    // --- State resolution (inlined from former MonitorLauncherStateResolver) ---
    private async Task<MonitorReadyState> ResolveReadyStateAsync()
    {
        var metadataState = await this.ReadValidatedAgentInfoAsync().ConfigureAwait(false);
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

        if (string.Equals(startupStatus, StatusStarting, StringComparison.OrdinalIgnoreCase) && metadataState.ProcessRunning)
        {
            return new MonitorReadyState(metadataState.EffectivePort, IsRunning: false, FromMetadata: false, IsStarting: true);
        }

        var port = metadataState.EffectivePort;
        var isRunning = await this.CheckHealthAsync(port).ConfigureAwait(false);
        return new MonitorReadyState(port, isRunning, FromMetadata: false);
    }

    private async Task<MonitorReadyState> GetReadyStateAsync()
    {
        var readyState = await this.ResolveReadyStateAsync().ConfigureAwait(false);
        if (readyState.IsRunning)
        {
            var source = readyState.FromMetadata ? "metadata" : "health check";
            this._logger?.LogDebug("Monitor is running on port {Port} via {Source}.", readyState.Port, source);
        }
        else if (readyState.IsStarting)
        {
            this._logger?.LogDebug("Monitor startup is still in progress on port {Port}.", readyState.Port);
        }
        else if (!string.IsNullOrWhiteSpace(readyState.StartupFailure))
        {
            this._logger?.LogDebug("Monitor startup reported failure: {StartupFailure}", readyState.StartupFailure);
        }
        else
        {
            this._logger?.LogDebug("Monitor not found on port {Port}.", readyState.Port);
        }

        return readyState;
    }

    private async Task<MonitorReadyState?> WaitForReadyStateAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int attempt = 0;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(MaxWaitSeconds))
        {
            attempt++;
            var readyState = await this.ResolveReadyStateAsync().ConfigureAwait(false);
            if (readyState.IsRunning)
            {
                this._logger?.LogDebug("Monitor is ready on port {Port} after {Elapsed:F1}s.", readyState.Port, stopwatch.Elapsed.TotalSeconds);
                return readyState;
            }

            if (!string.IsNullOrWhiteSpace(readyState.StartupFailure))
            {
                this._logger?.LogDebug("Monitor startup failed after {Elapsed:F1}s: {StartupFailure}", stopwatch.Elapsed.TotalSeconds, readyState.StartupFailure);
                return null;
            }

            if (attempt % 5 == 0)
            {
                this._logger?.LogDebug("Still waiting for Monitor... (elapsed: {Elapsed:F1}s)", stopwatch.Elapsed.TotalSeconds);
            }

            try
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                this._logger?.LogDebug(ex, "Monitor wait cancelled after {Elapsed:F1}s.", stopwatch.Elapsed.TotalSeconds);
                return null;
            }
        }

        this._logger?.LogDebug("Timed out waiting for Monitor.");
        return null;
    }

    private async Task<(MonitorInfo? Info, string? Path)> ReadAgentInfoAsync()
    {
        string? path = null;
        try
        {
            var candidatePath = this.GetMonitorInfoPath();
            path = File.Exists(candidatePath) ? candidatePath : null;
            if (path != null)
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<MonitorInfo>(json, CaseInsensitiveOptions);

                if (info != null)
                {
                    return (info, path);
                }

                this.QuarantineMonitorInfoPath(path, "Monitor metadata was empty or invalid; invalidating.");
            }

            return (null, path);
        }
        catch (JsonException ex)
        {
            this._logger?.LogDebug(ex, "Failed to parse monitor metadata: {Message}", ex.Message);
            if (path != null)
            {
                this.QuarantineMonitorInfoPath(path);
            }

            return (null, path);
        }
        catch (IOException ex)
        {
            this._logger?.LogDebug(ex, "Failed to read monitor metadata: {Message}", ex.Message);
            return (null, path);
        }
        catch (UnauthorizedAccessException ex)
        {
            this._logger?.LogDebug(ex, "Access denied reading monitor metadata: {Message}", ex.Message);
            return (null, path);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            this._logger?.LogDebug(ex, "Failed to load monitor metadata: {Message}", ex.Message);
            return (null, path);
        }
    }

    private async Task<MonitorMetadataState> ReadValidatedAgentInfoAsync()
    {
        var (info, path) = await this.ReadAgentInfoAsync().ConfigureAwait(false);
        if (info == null)
        {
            return new MonitorMetadataState(null, path, HealthOk: false, ProcessRunning: false);
        }

        var healthOk = await this.CheckHealthAsync(info.Port).ConfigureAwait(false);
        var processRunning = await this.CheckProcessRunningAsync(info.ProcessId).ConfigureAwait(false);
        var startupStatus = GetStartupStatus(info.Errors);

        if (healthOk && processRunning)
        {
            return new MonitorMetadataState(info, path, healthOk, processRunning);
        }

        if (string.Equals(startupStatus, StatusStarting, StringComparison.OrdinalIgnoreCase) && processRunning)
        {
            this._logger?.LogDebug("Monitor metadata indicates startup is still in progress.");
            return new MonitorMetadataState(info, path, healthOk, processRunning);
        }

        // Don't quarantine metadata that reports a startup failure while the process is still alive —
        // the failure IS the current state and callers need to read it.
        var startupFailure = GetStartupFailure(info.Errors);
        if (!string.IsNullOrWhiteSpace(startupFailure) && processRunning)
        {
            return new MonitorMetadataState(info, path, healthOk, processRunning);
        }

        this._logger?.LogDebug("Monitor metadata stale: health={HealthOk}, processRunning={ProcessRunning}, invalidating metadata", healthOk, processRunning);
        if (path != null)
        {
            this.QuarantineMonitorInfoPath(path);
        }

        return new MonitorMetadataState(info, path, healthOk, processRunning);
    }

    // --- Infrastructure methods ---
    private void QuarantineMonitorInfoPath(string infoPath, string? diagnosticMessage = null)
    {
        if (!string.IsNullOrEmpty(diagnosticMessage))
        {
            this._logger?.LogDebug("{DiagnosticMessage}", diagnosticMessage);
        }

        this.InvalidateMonitorInfoPath(infoPath);
    }

    private void InvalidateMonitorInfoPath(string infoPath)
    {
        var backupPath = infoPath + ".stale." + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        File.Move(infoPath, backupPath, overwrite: true);
        this._logger?.LogDebug("Backed up stale metadata to: {BackupPath}", backupPath);
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
        catch (IOException)
        {
            // Best-effort cleanup; file I/O errors are expected.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; permission errors are expected.
        }
    }

    private async Task<bool> CheckHealthAsync(int port)
    {
        if (this._healthCheckOverride != null)
        {
            return await this._healthCheckOverride(port).ConfigureAwait(false);
        }

        try
        {
            var response = await this._healthCheckHttpClient.GetAsync($"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}/api/health").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            this._logger?.LogDebug(ex, "Health check request failed on port {Port}: {Message}", port, ex.Message);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            this._logger?.LogDebug(ex, "Health check timed out on port {Port}: {Message}", port, ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            this._logger?.LogDebug(ex, "Health check failed on port {Port}: {Message}", port, ex.Message);
            return false;
        }
    }

    private Task<bool> CheckProcessRunningAsync(int processId)
    {
        if (this._processRunningOverride != null)
        {
            return this._processRunningOverride(processId);
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
        catch (ArgumentException ex)
        {
            this._logger?.LogDebug(ex, "Monitor process {ProcessId} was not found.", processId);
            return Task.FromResult(false);
        }
        catch (InvalidOperationException ex)
        {
            this._logger?.LogDebug(ex, "Failed to query monitor process {ProcessId}.", processId);
            return Task.FromResult(false);
        }
    }

    private string GetMonitorInfoPath()
    {
        if (!string.IsNullOrWhiteSpace(this._monitorInfoPathOverride))
        {
            return this._monitorInfoPathOverride;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Join(localAppData, CanonicalProductFolder, "monitor.json");
    }

    internal static string BuildLaunchMutexName()
    {
        return MutexNameBuilder.BuildGlobalName("AIUsageTracker_MonitorLaunch_");
    }

    private static string? GetStartupFailure(IReadOnlyList<string>? errors)
    {
        return errors?.FirstOrDefault(error =>
            !string.IsNullOrWhiteSpace(error) &&
            error.StartsWith(StartupStatusPrefix, StringComparison.OrdinalIgnoreCase) &&
            !error.Contains("running", StringComparison.OrdinalIgnoreCase) &&
            !error.Contains(StatusStarting, StringComparison.OrdinalIgnoreCase) &&
            !error.Contains("stopped", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetStartupStatus(IReadOnlyList<string>? errors)
    {
        var startupEntry = errors?.FirstOrDefault(error =>
            !string.IsNullOrWhiteSpace(error) &&
            error.StartsWith(StartupStatusPrefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(startupEntry))
        {
            return null;
        }

        return startupEntry[StartupStatusPrefix.Length..].Trim();
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
}
