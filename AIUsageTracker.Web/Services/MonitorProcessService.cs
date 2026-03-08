using System.Diagnostics;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Web.Services;

public class MonitorProcessService
{
    private readonly string _appDataPath;
    private readonly ILogger<MonitorProcessService> _logger;

    public MonitorProcessService(ILogger<MonitorProcessService> logger)
    {
        this._logger = logger;
        this._appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public async Task<(bool IsRunning, int Port)> GetAgentStatusAsync()
    {
        var detailed = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
        return (detailed.IsRunning, detailed.Port);
    }

    public async Task<(bool IsRunning, int Port, string Message, string? Error)> GetAgentStatusDetailedAsync()
    {
        var info = await this.GetAgentInfoAsync().ConfigureAwait(false);
        if (info == null)
        {
            return (false, 5000, "Monitor info file not found. Start Monitor to initialize it.", "agent-info-missing");
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            var response = await client.GetAsync($"http://localhost:{info.Port}/api/health").ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, info.Port, $"Healthy on port {info.Port}.", null);
            }

            return (false, info.Port, $"Health check failed ({(int)response.StatusCode} {response.ReasonPhrase}).", "health-check-failed");
        }
        catch (Exception ex)
        {
            return (false, info.Port, $"Monitor not reachable on port {info.Port}: {SimplifyExceptionMessage(ex)}", "monitor-unreachable");
        }
    }

    public async Task<bool> StartAgentAsync()
    {
        var detailed = await this.StartAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<(bool Success, string Message)> StartAgentDetailedAsync()
    {
        var status = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
        if (status.IsRunning)
        {
            return (true, $"Monitor already running on port {status.Port}.");
        }

        var info = await this.GetAgentInfoAsync().ConfigureAwait(false);
        int port = info?.Port ?? 5000;

        var agentPath = this.FindAgentExecutable();
        if (agentPath == null)
        {
            this._logger.LogError("Could not find agent executable");
            return (false, "Monitor executable not found. Build/publish Monitor first.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = agentPath,
                Arguments = $"--urls \"http://localhost:{port}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(agentPath),
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, "Failed to start monitor process.");
            }

            this._logger.LogInformation("Started agent from {Path}", agentPath);

            await Task.Delay(800).ConfigureAwait(false);
            var updated = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
            if (updated.IsRunning)
            {
                return (true, $"Monitor started on port {updated.Port}.");
            }

            return (false, $"Start requested, but monitor did not become healthy. {updated.Message}");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to start agent");
            return (false, $"Failed to start monitor: {SimplifyExceptionMessage(ex)}");
        }
    }

    public async Task<bool> StopAgentAsync()
    {
        var detailed = await this.StopAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<(bool Success, string Message)> StopAgentDetailedAsync()
    {
        var info = await this.GetAgentInfoAsync().ConfigureAwait(false);
        if (info == null)
        {
            this._logger.LogWarning("Cannot stop agent: agent.info not found");
            return (true, "Monitor already stopped (info file missing).");
        }

        try
        {
            var process = Process.GetProcessById(info.ProcessId);
            process.Kill();
            this._logger.LogInformation("Killed agent process {Pid}", info.ProcessId);
            return (true, $"Monitor stopped (PID {info.ProcessId}).");
        }
        catch (ArgumentException)
        {
            this._logger.LogInformation("Agent process {Pid} not currently running", info.ProcessId);
            return (true, $"Monitor process {info.ProcessId} already exited.");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to stop agent process {Pid}", info.ProcessId);
            return (false, $"Failed to stop monitor (PID {info.ProcessId}): {SimplifyExceptionMessage(ex)}");
        }
    }

    private static string ResolveAgentInfoPath(string appData)
    {
        var candidates = GetMonitorInfoCandidatePaths(appData).ToList();
        var existing = candidates
            .Where(File.Exists)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();

        return existing ?? candidates[0];
    }

    private static IEnumerable<string> GetMonitorInfoCandidatePaths(string appData)
    {
        return new[]
        {
            Path.Combine(appData, "AIUsageTracker", "monitor.json"),
            Path.Combine(appData, "AIUsageTracker", "Monitor", "monitor.json"),
            Path.Combine(appData, "AIUsageTracker", "Agent", "monitor.json"),
            Path.Combine(appData, "AIConsumptionTracker", "monitor.json"),
            Path.Combine(appData, "AIConsumptionTracker", "Monitor", "monitor.json"),
            Path.Combine(appData, "AIConsumptionTracker", "Agent", "monitor.json"),
        };
    }

    private static string SimplifyExceptionMessage(Exception ex)
    {
        if (ex is HttpRequestException)
        {
            return "HTTP request failed";
        }

        if (ex is TaskCanceledException)
        {
            return "timeout";
        }

        return ex.Message;
    }

    private string? FindAgentExecutable()
    {
        var baseDir = AppContext.BaseDirectory;

        var paths = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "AIUsageTracker.Monitor", "bin", "Debug", "net8.0", "AIUsageTracker.Monitor.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "AIUsageTracker.Monitor", "bin", "Release", "net8.0", "AIUsageTracker.Monitor.exe"),
            Path.Combine(baseDir, "AIUsageTracker.Monitor.exe"),

            // Legacy compatibility
            Path.Combine(baseDir, "..", "..", "..", "..", "AIConsumptionTracker.Agent", "bin", "Debug", "net8.0", "AIConsumptionTracker.Agent.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "AIConsumptionTracker.Agent", "bin", "Release", "net8.0", "AIConsumptionTracker.Agent.exe"),
            Path.Combine(baseDir, "AIConsumptionTracker.Agent.exe"),
        };

        return paths.FirstOrDefault(File.Exists);
    }

    private async Task<MonitorInfo?> GetAgentInfoAsync()
    {
        try
        {
            var infoFilePath = ResolveAgentInfoPath(this._appDataPath);
            if (File.Exists(infoFilePath))
            {
                var json = await File.ReadAllTextAsync(infoFilePath).ConfigureAwait(false);
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                return System.Text.Json.JsonSerializer.Deserialize<MonitorInfo>(json, options);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to read agent.info");
        }

        return null;
    }
}
