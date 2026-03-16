// <copyright file="MonitorLogPathResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Monitor.Logging;

internal static class MonitorLogPathResolver
{
    public static ResolvedMonitorLogPath Resolve(IAppPathProvider pathProvider, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        var preferredDirectory = pathProvider.GetLogDirectory();
        if (TryEnsureDirectory(preferredDirectory))
        {
            return new ResolvedMonitorLogPath(
                preferredDirectory!,
                BuildLogFilePath(preferredDirectory!, now),
                false,
                preferredDirectory);
        }

        var fallbackDirectory = Path.Combine(Path.GetTempPath(), "AIUsageTracker", "logs");
        if (TryEnsureDirectory(fallbackDirectory))
        {
            return new ResolvedMonitorLogPath(
                fallbackDirectory,
                BuildLogFilePath(fallbackDirectory, now),
                true,
                preferredDirectory);
        }

        throw new IOException(
            $"Unable to initialize monitor log directory. Preferred='{preferredDirectory ?? "<null>"}', Fallback='{fallbackDirectory}'.");
    }

    private static string BuildLogFilePath(string directory, DateTime now)
    {
        return Path.Combine(directory, $"monitor_{now:yyyy-MM-dd}.log");
    }

    private static bool TryEnsureDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    internal readonly record struct ResolvedMonitorLogPath(
        string LogDirectory,
        string LogFile,
        bool UsedFallback,
        string? PreferredDirectory);
}
