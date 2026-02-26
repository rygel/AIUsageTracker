using System.Diagnostics;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Web.Services;

public class MonitorProcessService
{
    private readonly string _infoFilePath;
    private readonly ILogger<MonitorProcessService> _logger;
    
    public MonitorProcessService(ILogger<MonitorProcessService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _infoFilePath = ResolveAgentInfoPath(appData);
    }

    public async Task<(bool isRunning, int port)> GetAgentStatusAsync()
    {
        var detailed = await GetAgentStatusDetailedAsync();
        return (detailed.isRunning, detailed.port);
    }

    public async Task<(bool isRunning, int port, string message, string? error)> GetAgentStatusDetailedAsync()
    {
        var info = await GetAgentInfoAsync();
        if (info == null)
        {
            return (false, 5000, "Monitor info file not found. Start Monitor to initialize it.", "agent-info-missing");
        }
        
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            var response = await client.GetAsync($"http://localhost:{info.Port}/api/health");
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
        var detailed = await StartAgentDetailedAsync();
        return detailed.success;
    }

    public async Task<(bool success, string message)> StartAgentDetailedAsync()
    {
        var status = await GetAgentStatusDetailedAsync();
        if (status.isRunning)
        {
            return (true, $"Monitor already running on port {status.port}.");
        }
        
        var info = await GetAgentInfoAsync();
        int port = info?.Port ?? 5000;
        
        var agentPath = FindAgentExecutable();
        if (agentPath == null) 
        {
            _logger.LogError("Could not find agent executable");
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
                WorkingDirectory = Path.GetDirectoryName(agentPath)
            };
            
            var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, "Failed to start monitor process.");
            }

            _logger.LogInformation("Started agent from {Path}", agentPath);

            await Task.Delay(800);
            var updated = await GetAgentStatusDetailedAsync();
            if (updated.isRunning)
            {
                return (true, $"Monitor started on port {updated.port}.");
            }

            return (false, $"Start requested, but monitor did not become healthy. {updated.message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start agent");
            return (false, $"Failed to start monitor: {SimplifyExceptionMessage(ex)}");
        }
    }

    public async Task<bool> StopAgentAsync()
    {
        var detailed = await StopAgentDetailedAsync();
        return detailed.success;
    }

    public async Task<(bool success, string message)> StopAgentDetailedAsync()
    {
        var info = await GetAgentInfoAsync();
        if (info == null)
        {
            _logger.LogWarning("Cannot stop agent: agent.info not found");
            return (true, "Monitor already stopped (info file missing).");
        }

        try
        {
            var process = Process.GetProcessById(info.ProcessId);
            process.Kill();
            _logger.LogInformation("Killed agent process {Pid}", info.ProcessId);
            return (true, $"Monitor stopped (PID {info.ProcessId}).");
        }
        catch (ArgumentException)
        {
            _logger.LogInformation("Agent process {Pid} not currently running", info.ProcessId);
            return (true, $"Monitor process {info.ProcessId} already exited.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop agent process {Pid}", info.ProcessId);
            return (false, $"Failed to stop monitor (PID {info.ProcessId}): {SimplifyExceptionMessage(ex)}");
        }
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
            if (File.Exists(_infoFilePath))
            {
                var json = await File.ReadAllTextAsync(_infoFilePath);
                var options = new System.Text.Json.JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                };
                return System.Text.Json.JsonSerializer.Deserialize<MonitorInfo>(json, options);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read agent.info");
        }
        return null;
    }

    private static string ResolveAgentInfoPath(string appData)
    {
        var primaryMonitorPath = Path.Combine(appData, "AIUsageTracker", "monitor.json");
        var legacyMonitorPath = Path.Combine(appData, "AIConsumptionTracker", "monitor.json");

        if (File.Exists(primaryMonitorPath))
        {
            return primaryMonitorPath;
        }

        if (File.Exists(legacyMonitorPath))
        {
            return legacyMonitorPath;
        }

        return primaryMonitorPath;
    }

}


