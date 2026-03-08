using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Core.MonitorClient;

public class MonitorLauncher
{
    private const int DefaultPort = 5000;
    private const int MaxWaitSeconds = 30;
    private const int StopWaitSeconds = 5;
    private static ILogger<MonitorLauncher>? _logger;
    private static readonly SemaphoreSlim StartupSemaphore = new(1, 1);
    private static Func<IEnumerable<string>>? _monitorInfoCandidatePathsOverride;
    private static Func<int, Task<bool>>? _healthCheckOverride;
    private static Func<int, Task<bool>>? _processRunningOverride;

    public static void SetLogger(ILogger<MonitorLauncher> logger) => _logger = logger;

    internal static IDisposable PushTestOverrides(
        IEnumerable<string>? monitorInfoCandidatePaths = null,
        Func<int, Task<bool>>? healthCheckAsync = null,
        Func<int, Task<bool>>? processRunningAsync = null)
    {
        var previousCandidatePaths = _monitorInfoCandidatePathsOverride;
        var previousHealthCheck = _healthCheckOverride;
        var previousProcessCheck = _processRunningOverride;

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

        return new TestOverrideScope(() =>
        {
            _monitorInfoCandidatePathsOverride = previousCandidatePaths;
            _healthCheckOverride = previousHealthCheck;
            _processRunningOverride = previousProcessCheck;
        });
    }


    private static async Task<MonitorInfo?> GetAgentInfoAsync()
    {
        string? path = null;

        try
        {
            path = GetExistingAgentInfoPath();

            if (path != null)
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<MonitorInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (info != null)
                {
                    return info;
                }

                MonitorService.LogDiagnostic($"Monitor metadata at '{path}' was empty or invalid; invalidating metadata.");
                InvalidateMonitorInfoPath(path);
            }
            
            return null;
        }
        catch (JsonException ex)
        {
            MonitorService.LogDiagnostic($"Failed to parse monitor metadata: {ex.Message}");
            if (path != null)
            {
                InvalidateMonitorInfoPath(path);
            }

            return null;
        }
        catch (IOException ex)
        {
            MonitorService.LogDiagnostic($"Failed to read monitor metadata: {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            MonitorService.LogDiagnostic($"Access denied reading monitor metadata: {ex.Message}");
            return null;
        }
        catch
        {
            MonitorService.LogDiagnostic("Failed to load monitor metadata for an unknown reason.");
            return null;
        }
    }

    private static async Task<(MonitorInfo? Info, int Port, bool IsRunning)> ResolveMonitorStateAsync()
    {
        var info = await GetAndValidateMonitorInfoAsync().ConfigureAwait(false);
        if (info != null)
        {
            return (info, info.Port, true);
        }

        var port = DefaultPort;
        var isRunning = await CheckHealthAsync(port).ConfigureAwait(false);
        return (null, port, isRunning);
    }

    public static async Task<int> GetAgentPortAsync()
    {
        var (_, port, _) = await ResolveMonitorStateAsync().ConfigureAwait(false);
        return port;
    }

    public static async Task<bool> IsAgentRunningAsync()
    {
        var (info, port, isRunning) = await ResolveMonitorStateAsync().ConfigureAwait(false);
        if (info != null)
        {
            MonitorService.LogDiagnostic($"Monitor is running on port {info.Port}");
            return true;
        }

        MonitorService.LogDiagnostic($"Checking Monitor status on port: {port}");
        if (isRunning)
        {
            MonitorService.LogDiagnostic($"Monitor is running on port {port}");
            return true;
        }

        MonitorService.LogDiagnostic($"Monitor not found on port {port}.");
        return false;
    }
    
    public static async Task<(bool IsRunning, int Port)> IsAgentRunningWithPortAsync()
    {
        var (info, port, isRunning) = await ResolveMonitorStateAsync().ConfigureAwait(false);
        if (info != null)
        {
            return (true, info.Port);
        }

        MonitorService.LogDiagnostic($"Probing Monitor port: {port}");

        if (isRunning)
        {
            return (true, port);
        }
        
        MonitorService.LogDiagnostic($"Monitor not found on port {port}.");
        return (false, port);
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
        var info = await GetAgentInfoAsync().ConfigureAwait(false);
        if (info == null)
        {
            return null;
        }

        var port = info.Port;
        var processId = info.ProcessId;

        var healthOk = await CheckHealthAsync(port).ConfigureAwait(false);
        var processRunning = await CheckProcessRunningAsync(processId).ConfigureAwait(false);

        if (healthOk && processRunning)
        {
            return info;
        }

        MonitorService.LogDiagnostic($"Monitor metadata stale: health={healthOk}, processRunning={processRunning}, invalidating metadata");

        await InvalidateMonitorInfoAsync().ConfigureAwait(false);
        return null;
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
            var (validatedInfo, port, isRunning) = await ResolveMonitorStateAsync().ConfigureAwait(false);
            if (validatedInfo != null)
            {
                MonitorService.LogDiagnostic($"Monitor already running on port {validatedInfo.Port}; skipping start.");
                return true;
            }

            if (isRunning)
            {
                MonitorService.LogDiagnostic($"Monitor already responding on port {port}; skipping start.");
                return true;
            }

            var monitorExeName = OperatingSystem.IsWindows()
                ? "AIUsageTracker.Monitor.exe"
                : "AIUsageTracker.Monitor";
            // Try to find Agent executable
            var possiblePaths = new[]
            {
                // Development paths
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIUsageTracker.Monitor", "bin", "Debug", "net8.0", monitorExeName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIUsageTracker.Monitor", "bin", "Release", "net8.0", monitorExeName),
                // Installed paths
                Path.Combine(AppContext.BaseDirectory, monitorExeName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AIUsageTracker", monitorExeName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageTracker", monitorExeName),
            };

            MonitorService.LogDiagnostic($"Locating Monitor executable (checked {possiblePaths.Length} common locations)...");
            var agentPath = possiblePaths.FirstOrDefault(File.Exists);

            if (agentPath == null)
            {
                MonitorService.LogDiagnostic("Monitor executable not found. Searching for project directory for 'dotnet run'...");
                // Try dotnet run in the Agent project directory
                var agentProjectDir = FindAgentProjectDirectory();
                if (agentProjectDir != null)
                {
                    MonitorService.LogDiagnostic($"Found Monitor project at: {agentProjectDir}. Launching via 'dotnet run'...");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{agentProjectDir}\" --urls \"http://localhost:{port}\" -- --debug",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = agentProjectDir,
                    };

                    // Prevent MSBuild from leaving zombie processes that hold file locks
                    psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
                    psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

                    return TryStartMonitorProcess(psi, "dotnet run");
                }

                MonitorService.LogDiagnostic("Could not find Monitor executable or project directory.");
                return false;
            }

            MonitorService.LogDiagnostic($"Monitor executable found at: {agentPath}. Launching...");
            var startInfo = new ProcessStartInfo
            {
                FileName = agentPath,
                Arguments = $"--urls \"http://localhost:{port}\" --debug",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(agentPath),
            };

            return TryStartMonitorProcess(startInfo, agentPath);
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

    private static bool TryStartMonitorProcess(ProcessStartInfo startInfo, string launchTarget)
    {
        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                MonitorService.LogDiagnostic($"Monitor launch returned no process for target '{launchTarget}'.");
                return false;
            }

            MonitorService.LogDiagnostic($"Monitor process started via '{launchTarget}' (PID {process.Id}).");
            return true;
        }
        catch (Exception ex)
        {
            MonitorService.LogDiagnostic($"Failed to launch Monitor via '{launchTarget}': {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> EnsureAgentRunningAsync(CancellationToken cancellationToken = default)
    {
        var (isRunning, port) = await IsAgentRunningWithPortAsync().ConfigureAwait(false);
        if (isRunning)
        {
            MonitorService.LogDiagnostic($"Monitor already ready on port {port}; no startup needed.");
            return true;
        }

        var started = await StartAgentAsync().ConfigureAwait(false);
        if (!started)
        {
            return false;
        }

        return await WaitForAgentAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<bool> StopAgentAsync()
    {
        try
        {
            var info = await GetAgentInfoAsync().ConfigureAwait(false);
            var targetPort = info?.Port > 0 ? info.Port : await GetAgentPortAsync().ConfigureAwait(false);
            if (info?.ProcessId > 0)
            {
                if (await TryStopProcessAsync(info.ProcessId).ConfigureAwait(false))
                {
                    return true;
                }
            }

            // Fallback: try to find and kill by process name
            var processes = Process.GetProcessesByName("AIUsageTracker.Monitor")
                .ToArray();
            var stoppedAny = false;
            foreach (var process in processes)
            {
                using (process)
                {
                    if (await TryStopProcessAsync(process).ConfigureAwait(false))
                    {
                        stoppedAny = true;
                    }
                }
            }

            if (stoppedAny)
            {
                return true;
            }

            return !await CheckHealthAsync(targetPort).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to stop Agent");
            return false;
        }
    }

    private static async Task<bool> TryStopProcessAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return await TryStopProcessAsync(process).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception ex)
        {
            MonitorService.LogDiagnostic($"Failed to stop process {processId}: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TryStopProcessAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(StopWaitSeconds)).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            MonitorService.LogDiagnostic($"Timed out waiting for process {process.Id} to exit.");
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Exception ex)
        {
            MonitorService.LogDiagnostic($"Failed to stop process {process.Id}: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default)
    {
        MonitorService.LogDiagnostic($"Waiting for Monitor to start (max {MaxWaitSeconds}s)...");
        var stopwatch = Stopwatch.StartNew();
        int attempt = 0;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(MaxWaitSeconds))
        {
            attempt++;
            var (isRunning, port) = await IsAgentRunningWithPortAsync().ConfigureAwait(false);
            if (isRunning)
            {
                MonitorService.LogDiagnostic($"Monitor is ready on port {port} after {stopwatch.Elapsed.TotalSeconds:F1}s.");
                return true;
            }

            if (attempt % 5 == 0) // Log status every 1 second (5 * 200ms)
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
                return false;
            }
        }

        MonitorService.LogDiagnostic("Timed out waiting for Monitor.");
        return false;
    }

    private static string? FindAgentProjectDirectory()
    {
        var currentDir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var agentDir = Path.Combine(currentDir, "AIUsageTracker.Monitor");
            if (Directory.Exists(agentDir) && File.Exists(Path.Combine(agentDir, "AIUsageTracker.Monitor.csproj")))
            {
                return agentDir;
            }

            var parent = Directory.GetParent(currentDir);
            if (parent == null)
            {
                break;
            }

            currentDir = parent.FullName;
        }

        // Try common locations
        var possibleRoots = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
        };

        foreach (var root in possibleRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var agentDir = Path.Combine(root, "AIUsageTracker.Monitor");
            if (Directory.Exists(agentDir) && File.Exists(Path.Combine(agentDir, "AIUsageTracker.Monitor.csproj")))
            {
                return agentDir;
            }
        }

        return null;
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

        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return MonitorInfoPathCatalog.GetReadCandidatePaths(appDataRoot, userProfileRoot);
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
