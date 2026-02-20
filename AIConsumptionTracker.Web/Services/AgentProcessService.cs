using System.Diagnostics;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Web.Services;

public class AgentProcessService
{
    private readonly string _infoFilePath;
    private readonly ILogger<AgentProcessService> _logger;
    
    public AgentProcessService(ILogger<AgentProcessService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _infoFilePath = Path.Combine(appData, "AIConsumptionTracker", "Agent", "agent.json");
    }

    public async Task<(bool isRunning, int port)> GetAgentStatusAsync()
    {
        var info = await GetAgentInfoAsync();
        if (info == null) return (false, 5000);
        
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            var response = await client.GetAsync($"http://localhost:{info.Port}/api/health");
            return (response.IsSuccessStatusCode, info.Port);
        }
        catch
        {
            return (false, info.Port);
        }
    }

    public async Task<bool> StartAgentAsync()
    {
        var (isRunning, _) = await GetAgentStatusAsync();
        if (isRunning) return true;
        
        var info = await GetAgentInfoAsync();
        int port = info?.Port ?? 5000;
        
        var agentPath = FindAgentExecutable();
        if (agentPath == null) 
        {
            _logger.LogError("Could not find agent executable");
            return false;
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
            
            Process.Start(startInfo);
            _logger.LogInformation("Started agent from {Path}", agentPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start agent");
            return false;
        }
    }

    public async Task<bool> StopAgentAsync()
    {
        var info = await GetAgentInfoAsync();
        if (info == null)
        {
            _logger.LogWarning("Cannot stop agent: agent.info not found");
            return true; // Assume already stopped if no info
        }

        try
        {
            var process = Process.GetProcessById(info.ProcessId);
            process.Kill();
            _logger.LogInformation("Killed agent process {Pid}", info.ProcessId);
            return true;
        }
        catch (ArgumentException)
        {
            _logger.LogInformation("Agent process {Pid} not currently running", info.ProcessId);
            return true; // Already gone
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop agent process {Pid}", info.ProcessId);
            return false;
        }
    }

    private string? FindAgentExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        
        var paths = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "AIConsumptionTracker.Agent", "bin", "Debug", "net8.0", "AIConsumptionTracker.Agent.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "AIConsumptionTracker.Agent", "bin", "Release", "net8.0", "AIConsumptionTracker.Agent.exe"),
            Path.Combine(baseDir, "AIConsumptionTracker.Agent.exe"),
        };
        
        return paths.FirstOrDefault(File.Exists);
    }

    private async Task<AgentInfo?> GetAgentInfoAsync()
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
                return System.Text.Json.JsonSerializer.Deserialize<AgentInfo>(json, options);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read agent.info");
        }
        return null;
    }

}
