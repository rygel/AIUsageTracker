using AIUsageTracker.Core.Interfaces;
using System.IO;

namespace AIUsageTracker.Infrastructure.Helpers;

public class DefaultAppPathProvider : IAppPathProvider
{
    private const string AppName = "AIUsageTracker";

    public string GetAppDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppName);
    }

    public string GetDatabasePath()
    {
        return Path.Combine(this.GetAppDataRoot(), "usage.db");
    }

    public string GetLogDirectory()
    {
        return Path.Combine(this.GetAppDataRoot(), "logs");
    }

    public string GetAuthFilePath()
    {
        var home = this.GetUserProfileRoot();
        return Path.Combine(home, ".opencode", "auth.json");
    }

    public string GetPreferencesFilePath()
    {
        return Path.Combine(this.GetAppDataRoot(), "preferences.json");
    }

    public string GetProviderConfigFilePath()
    {
        return Path.Combine(this.GetAppDataRoot(), "providers.json");
    }

    public string GetUserProfileRoot()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
