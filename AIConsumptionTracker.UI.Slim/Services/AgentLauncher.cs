using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace AIConsumptionTracker.UI.Slim.Services;

public class AgentLauncher
{
    private const int DefaultPort = 5000;
    private const int MaxWaitSeconds = 30;

    public static async Task<int> GetAgentPortAsync()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var portFile = Path.Combine(appData, "AIConsumptionTracker", "Agent", "agent.port");
            
            if (File.Exists(portFile))
            {
                var portStr = await File.ReadAllTextAsync(portFile);
                if (int.TryParse(portStr, out int port))
                {
                    return port;
                }
            }
            
            return DefaultPort;
        }
        catch
        {
            return DefaultPort;
        }
    }

    public static async Task<bool> IsAgentRunningAsync()
    {
        // Try the configured port first, then fallback ports
        var portsToTry = new List<int>();
        
        // Add the port from file first
        var configuredPort = await GetAgentPortAsync();
        portsToTry.Add(configuredPort);
        
        // Add fallback ports
        if (configuredPort != DefaultPort)
            portsToTry.Add(DefaultPort);
        for (int p = 5001; p <= 5010; p++)
            portsToTry.Add(p);
        
        foreach (var port in portsToTry)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await client.GetAsync($"http://localhost:{port}/api/health");
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
                // Continue to next port
            }
        }
        
        return false;
    }
    
    public static async Task<(bool isRunning, int port)> IsAgentRunningWithPortAsync()
    {
        // Try the configured port first, then fallback ports
        var portsToTry = new List<int>();
        
        // Add the port from file first
        var configuredPort = await GetAgentPortAsync();
        portsToTry.Add(configuredPort);
        
        // Add fallback ports
        if (configuredPort != DefaultPort)
            portsToTry.Add(DefaultPort);
        for (int p = 5001; p <= 5010; p++)
            portsToTry.Add(p);
        
        foreach (var port in portsToTry)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await client.GetAsync($"http://localhost:{port}/api/health");
                if (response.IsSuccessStatusCode)
                    return (true, port);
            }
            catch
            {
                // Continue to next port
            }
        }
        
        return (false, DefaultPort);
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

            var agentPath = possiblePaths.FirstOrDefault(File.Exists);

            if (agentPath == null)
            {
                // Try dotnet run in the Agent project directory
                var agentProjectDir = FindAgentProjectDirectory();
                if (agentProjectDir != null)
                {
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

                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = agentPath,
                Arguments = $"--urls \"http://localhost:{port}\" --debug",
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(agentPath)
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start Agent: {ex.Message}");
            return false;
        }
    }
    
    // Backward compatibility - synchronous wrapper
    public static bool StartAgent()
    {
        return StartAgentAsync().GetAwaiter().GetResult();
    }

    public static async Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        while ((DateTime.Now - startTime).TotalSeconds < MaxWaitSeconds)
        {
            var (isRunning, port) = await IsAgentRunningWithPortAsync();
            if (isRunning)
                return true;

            await Task.Delay(500, cancellationToken);
        }
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
