using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Core.AgentClient;

public class AgentLauncher
{
    private const int DefaultPort = 5000;
    private const int MaxWaitSeconds = 30;


    private static async Task<AgentInfo?> GetAgentInfoAsync()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var agentDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");
            var jsonFile = Path.Combine(agentDir, "agent.json");
            
            string? path = null;
            if (File.Exists(jsonFile)) path = jsonFile;

            if (path != null)
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<AgentInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<int> GetAgentPortAsync()
    {
        var info = await GetAgentInfoAsync();
        return info?.Port > 0 ? info.Port : DefaultPort;
    }

    public static async Task<bool> IsAgentRunningAsync()
    {
        var port = await GetAgentPortAsync();
        AgentService.LogDiagnostic($"Checking Agent status on port: {port}");
        
        if (await CheckHealthAsync(port))
        {
            AgentService.LogDiagnostic($"Agent is running on port {port}");
            return true;
        }
        
        AgentService.LogDiagnostic($"Agent not found on port {port}.");
        return false;
    }
    
    public static async Task<(bool isRunning, int port)> IsAgentRunningWithPortAsync()
    {
        var port = await GetAgentPortAsync();
        AgentService.LogDiagnostic($"Probing Agent port: {port}");
        
        if (await CheckHealthAsync(port))
        {
            return (true, port);
        }
        
        AgentService.LogDiagnostic($"Agent not found on port {port}.");
        return (false, port);
    }

    private static async Task<bool> CheckHealthAsync(int port)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var response = await client.GetAsync($"http://localhost:{port}/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> StartAgentAsync()
    {
        try
        {
            // Get the port to use
            var port = await GetAgentPortAsync();
            
            // Try to find Agent executable
            var possiblePaths = new[]
            {
                // Development paths
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIConsumptionTracker.Agent", "bin", "Debug", "net8.0-windows10.0.17763.0", "AIConsumptionTracker.Agent.exe"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIConsumptionTracker.Agent", "bin", "Release", "net8.0-windows10.0.17763.0", "AIConsumptionTracker.Agent.exe"),
                // Installed paths
                Path.Combine(AppContext.BaseDirectory, "AIConsumptionTracker.Agent.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AIConsumptionTracker", "AIConsumptionTracker.Agent.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIConsumptionTracker", "AIConsumptionTracker.Agent.exe"),
            };

            AgentService.LogDiagnostic($"Searching for Agent executable. Tried {possiblePaths.Length} paths.");
            var agentPath = possiblePaths.FirstOrDefault(File.Exists);

            if (agentPath == null)
            {
                AgentService.LogDiagnostic("Agent executable not found. Searching for project directory for 'dotnet run'...");
                // Try dotnet run in the Agent project directory
                var agentProjectDir = FindAgentProjectDirectory();
                if (agentProjectDir != null)
                {
                    AgentService.LogDiagnostic($"Found Agent project at: {agentProjectDir}. Launching via 'dotnet run'...");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{agentProjectDir}\" --urls \"http://localhost:{port}\" -- --debug",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WorkingDirectory = agentProjectDir
                    };
                    Process.Start(psi);
                    return true;
                }

                AgentService.LogDiagnostic("Could not find Agent executable or project directory.");
                return false;
            }

            AgentService.LogDiagnostic($"Agent executable found at: {agentPath}. Launching...");
            var startInfo = new ProcessStartInfo
            {
                FileName = agentPath,
                Arguments = $"--urls \"http://localhost:{port}\" --debug",
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(agentPath)
            };

            Process.Start(startInfo);
            AgentService.LogDiagnostic("Agent process started.");
            return true;
        }
        catch (Exception ex)
        {
            AgentService.LogDiagnostic($"Failed to start Agent: {ex.Message}");
            return false;
        }
    }
    
    // Backward compatibility - synchronous wrapper
    public static bool StartAgent()
    {
        return StartAgentAsync().GetAwaiter().GetResult();
    }

    public static async Task<bool> StopAgentAsync()
    {
        try
        {
            var info = await GetAgentInfoAsync();
            if (info?.ProcessId > 0)
            {
                try
                {
                    var process = Process.GetProcessById(info.ProcessId);
                    process.Kill();
                    await process.WaitForExitAsync();
                    return true;
                }
                catch (ArgumentException)
                {
                    // Process already exited
                    return true;
                }
            }
            
            // Fallback: try to find and kill by process name
            var processes = Process.GetProcessesByName("AIConsumptionTracker.Agent");
            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    await process.WaitForExitAsync();
                }
                catch { /* Ignore errors */ }
            }
            
            return processes.Length > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to stop Agent: {ex.Message}");
            return false;
        }
    }
    
    // Backward compatibility - synchronous wrapper
    public static bool StopAgent()
    {
        return StopAgentAsync().GetAwaiter().GetResult();
    }

    public static async Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default)
    {
        AgentService.LogDiagnostic($"Waiting for Agent to start (max {MaxWaitSeconds}s)...");
        var startTime = DateTime.Now;
        int attempt = 0;
        while ((DateTime.Now - startTime).TotalSeconds < MaxWaitSeconds)
        {
            attempt++;
            var (isRunning, port) = await IsAgentRunningWithPortAsync();
            if (isRunning)
            {
                AgentService.LogDiagnostic($"Agent is ready on port {port} after {(DateTime.Now - startTime).TotalSeconds:F1}s.");
                return true;
            }

            if (attempt % 5 == 0) // Log status every 1 second (5 * 200ms)
                AgentService.LogDiagnostic($"Still waiting for Agent... (elapsed: {(DateTime.Now - startTime).TotalSeconds:F1}s)");

            await Task.Delay(200, cancellationToken);
        }
        AgentService.LogDiagnostic("Timed out waiting for Agent.");
        return false;
    }

    private static string? FindAgentProjectDirectory()
    {
        var currentDir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var agentDir = Path.Combine(currentDir, "AIConsumptionTracker.Agent");
            if (Directory.Exists(agentDir) && File.Exists(Path.Combine(agentDir, "AIConsumptionTracker.Agent.csproj")))
                return agentDir;

            var parent = Directory.GetParent(currentDir);
            if (parent == null) break;
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
            if (!Directory.Exists(root)) continue;

            var agentDir = Path.Combine(root, "AIConsumptionTracker.Agent");
            if (Directory.Exists(agentDir) && File.Exists(Path.Combine(agentDir, "AIConsumptionTracker.Agent.csproj")))
                return agentDir;
        }

        return null;
    }
}
