// <copyright file="MonitorExecutableCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public static class MonitorExecutableCatalog
{
    private const string MonitorProjectDirectoryName = "AIUsageTracker.Monitor";
    private const string MonitorProjectFileName = "AIUsageTracker.Monitor.csproj";

    public static IReadOnlyList<string> GetExecutableCandidates(string baseDirectory, string monitorExecutableName)
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

    public static string? FindProjectDirectory(string baseDirectory)
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
}
