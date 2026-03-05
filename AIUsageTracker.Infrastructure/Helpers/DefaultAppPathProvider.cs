using AIUsageTracker.Core.Interfaces;
using System.Collections.Generic;
using System.IO;

namespace AIUsageTracker.Infrastructure.Helpers;

public class DefaultAppPathProvider : IAppPathProvider
{
    private const string AppName = "AIUsageTracker";
    private const string LegacyAppName = "AIConsumptionTracker";

    public string GetAppDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var primary = Path.Combine(localAppData, AppName);
        var legacy = Path.Combine(localAppData, LegacyAppName);

        return Directory.Exists(primary) ? primary : (Directory.Exists(legacy) ? legacy : primary);
    }

    public string GetDatabasePath()
    {
        return Path.Combine(GetAppDataRoot(), "usage.db");
    }

    public string GetLogDirectory()
    {
        return Path.Combine(GetAppDataRoot(), "logs");
    }

    public string GetAuthFilePath()
    {
        // Auth is typically in UserProfile for CLI tools
        var home = GetUserProfileRoot();
        var primary = Path.Combine(home, ".opencode", "auth.json");
        var legacy = Path.Combine(home, ".ai-consumption-tracker", "auth.json");
        
        return File.Exists(primary) ? primary : (File.Exists(legacy) ? legacy : primary);
    }

    public string GetPreferencesFilePath()
    {
        return Path.Combine(GetAppDataRoot(), "preferences.json");
    }

    public string GetProviderConfigFilePath()
    {
        return Path.Combine(GetAppDataRoot(), "providers.json");
    }

    public string GetUserProfileRoot()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public IEnumerable<string> GetMonitorInfoCandidatePaths()
    {
        var appDataRoot = GetAppDataRoot();
        var userProfileRoot = GetUserProfileRoot();

        return new[]
        {
            Path.Combine(appDataRoot, "monitor.json"),
            Path.Combine(appDataRoot, "Monitor", "monitor.json"),
            Path.Combine(appDataRoot, "Agent", "monitor.json"),
            Path.Combine(userProfileRoot, ".ai-consumption-tracker", "monitor.json"),
            Path.Combine(userProfileRoot, ".opencode", "monitor.json")
        };
    }
}
