using System;
using System.IO;

namespace AIUsageTracker.Core.Utilities;

public static class AppPaths
{
    public static string GetAppDataDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public static string GetAppDirectory()
    {
        return Path.Combine(GetAppDataDirectory(), "AIUsageTracker");
    }

    public static string GetLegacyAppDirectory()
    {
        return Path.Combine(GetAppDataDirectory(), "AIConsumptionTracker");
    }

    public static string GetLogsDirectory()
    {
        return Path.Combine(GetAppDirectory(), "logs");
    }

    public static string EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    public static string GetDatabasePath(bool preferLegacy = false)
    {
        var appDir = GetAppDirectory();
        var legacyDir = GetLegacyAppDirectory();

        var primaryDb = Path.Combine(appDir, "usage.db");
        var legacyDb = Path.Combine(legacyDir, "usage.db");

        if (preferLegacy && File.Exists(legacyDb))
        {
            return Path.Combine(legacyDir, "usage.db");
        }

        if (File.Exists(primaryDb))
        {
            return primaryDb;
        }

        if (File.Exists(legacyDb))
        {
            return legacyDb;
        }

        return Path.Combine(appDir, "usage.db");
    }

    public static string ResolveDatabaseDirectory()
    {
        return ResolveDatabaseDirectory(GetAppDataDirectory());
    }

    private static string ResolveDatabaseDirectory(string appData)
    {
        var primaryDir = Path.Combine(appData, "AIUsageTracker");
        var legacyDir = Path.Combine(appData, "AIConsumptionTracker");

        var primaryDb = Path.Combine(primaryDir, "usage.db");
        var legacyDb = Path.Combine(legacyDir, "usage.db");

        if (File.Exists(primaryDb))
        {
            return primaryDir;
        }

        if (File.Exists(legacyDb))
        {
            return legacyDir;
        }

        return primaryDir;
    }

    public static string GetMonitorLogFilePath(DateTime date)
    {
        var logsDir = EnsureDirectoryExists(GetLogsDirectory());
        return Path.Combine(logsDir, $"monitor_{date:yyyy-MM-dd}.log");
    }

    public static string GetConfigFilePath(string configName)
    {
        return Path.Combine(GetAppDirectory(), $"{configName}.json");
    }
}
