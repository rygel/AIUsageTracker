// <copyright file="MonitorLauncherProcessController.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Core.MonitorClient;

internal static class MonitorLauncherProcessController
{
    private const string MonitorProjectDirectoryName = "AIUsageTracker.Monitor";
    private const string MonitorProjectFileName = "AIUsageTracker.Monitor.csproj";

    public static LaunchPlan? TryResolveLaunchPlan(int port)
    {
        var monitorExeName = OperatingSystem.IsWindows()
            ? "AIUsageTracker.Monitor.exe"
            : "AIUsageTracker.Monitor";
        var possiblePaths = GetExecutableCandidates(AppContext.BaseDirectory, monitorExeName);

        MonitorService.LogDiagnostic($"Locating Monitor executable (checked {possiblePaths.Count} common locations)...");
        var agentPath = possiblePaths.FirstOrDefault(File.Exists);

        if (agentPath != null)
        {
            MonitorService.LogDiagnostic($"Monitor executable found at: {agentPath}. Launching...");
            return new LaunchPlan(CreateExecutableLaunchInfo(agentPath, port), agentPath);
        }

        MonitorService.LogDiagnostic("Monitor executable not found. Searching for project directory for 'dotnet run'...");
        var agentProjectDir = FindProjectDirectory(AppContext.BaseDirectory);
        if (agentProjectDir == null)
        {
            MonitorService.LogDiagnostic("Could not find Monitor executable or project directory.");
            return null;
        }

        MonitorService.LogDiagnostic($"Found Monitor project at: {agentProjectDir}. Launching via 'dotnet run'...");
        return new LaunchPlan(CreateProjectLaunchInfo(agentProjectDir, port), "dotnet run");
    }

    public static bool TryStartMonitorProcess(ProcessStartInfo startInfo, string launchTarget)
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

    public static async Task<bool> StopAgentAsync(
        MonitorInfo? info,
        int fallbackPort,
        int stopWaitSeconds,
        Func<int, Task<bool>> checkHealthAsync,
        Func<int, Task<bool>> stopProcessAsync,
        Func<Task<bool>> stopNamedProcessesAsync,
        Func<Task> invalidateMonitorInfoAsync,
        ILogger<MonitorLauncher>? logger)
    {
        try
        {
            if (await TryStopKnownMonitorProcessAsync(info, stopProcessAsync).ConfigureAwait(false))
            {
                await invalidateMonitorInfoAsync().ConfigureAwait(false);
                return true;
            }

            if (await stopNamedProcessesAsync().ConfigureAwait(false))
            {
                await invalidateMonitorInfoAsync().ConfigureAwait(false);
                return true;
            }

            var isStillHealthy = await checkHealthAsync(fallbackPort).ConfigureAwait(false);
            if (!isStillHealthy)
            {
                await invalidateMonitorInfoAsync().ConfigureAwait(false);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to stop Agent");
            return false;
        }

        static async Task<bool> TryStopKnownMonitorProcessAsync(MonitorInfo? monitorInfo, Func<int, Task<bool>> stopByProcessIdAsync)
        {
            if (monitorInfo?.ProcessId > 0)
            {
                return await stopByProcessIdAsync(monitorInfo.ProcessId).ConfigureAwait(false);
            }

            return false;
        }
    }

    public static async Task<bool> TryStopNamedProcessesAsync(int stopWaitSeconds, Func<Task<bool>>? stopNamedProcessesOverride)
    {
        if (stopNamedProcessesOverride != null)
        {
            return await stopNamedProcessesOverride().ConfigureAwait(false);
        }

        var processes = Process.GetProcessesByName("AIUsageTracker.Monitor").ToArray();
        var stoppedAny = false;
        foreach (var process in processes)
        {
            using (process)
            {
                if (await TryStopProcessAsync(process, stopWaitSeconds).ConfigureAwait(false))
                {
                    stoppedAny = true;
                }
            }
        }

        return stoppedAny;
    }

    public static async Task<bool> TryStopProcessAsync(
        int processId,
        int stopWaitSeconds,
        Func<int, Task<bool>>? stopProcessOverride)
    {
        if (stopProcessOverride != null)
        {
            return await stopProcessOverride(processId).ConfigureAwait(false);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return await TryStopProcessAsync(process, stopWaitSeconds).ConfigureAwait(false);
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

    private static ProcessStartInfo CreateExecutableLaunchInfo(string agentPath, int port)
    {
        return new ProcessStartInfo
        {
            FileName = agentPath,
            Arguments = $"--urls \"http://localhost:{port}\" --debug",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(agentPath),
        };
    }

    private static IReadOnlyList<string> GetExecutableCandidates(string baseDirectory, string monitorExecutableName)
    {
        return new[]
        {
            Path.Combine(baseDirectory, "..", "..", "..", "..", "AIUsageTracker.Monitor", "bin", "Debug", "net8.0", monitorExecutableName),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "AIUsageTracker.Monitor", "bin", "Release", "net8.0", monitorExecutableName),
            Path.Combine(baseDirectory, monitorExecutableName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AIUsageTracker", monitorExecutableName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageTracker", monitorExecutableName),
        };
    }

    private static string? FindProjectDirectory(string baseDirectory)
    {
        var currentDir = baseDirectory;
        for (var i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(currentDir, MonitorProjectDirectoryName);
            if (LooksLikeProjectDirectory(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(currentDir);
            if (parent == null)
            {
                break;
            }

            currentDir = parent.FullName;
        }

        foreach (var root in new[] { Environment.CurrentDirectory, baseDirectory })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var candidate = Path.Combine(root, MonitorProjectDirectoryName);
            if (LooksLikeProjectDirectory(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool LooksLikeProjectDirectory(string candidate)
    {
        return Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, MonitorProjectFileName));
    }

    private static ProcessStartInfo CreateProjectLaunchInfo(string agentProjectDir, int port)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{agentProjectDir}\" --urls \"http://localhost:{port}\" -- --debug",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = agentProjectDir,
        };

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        return startInfo;
    }

    private static async Task<bool> TryStopProcessAsync(Process process, int stopWaitSeconds)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(stopWaitSeconds)).ConfigureAwait(false);
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

    internal readonly record struct LaunchPlan(ProcessStartInfo StartInfo, string LaunchTarget);
}
