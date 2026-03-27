// <copyright file="DefaultAppPathProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Infrastructure.Helpers;

public class DefaultAppPathProvider : IAppPathProvider
{
    private const string AppDirectoryName = "AIUsageTracker";

    public string GetAppDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return GetCanonicalAppDataRoot(localAppData);
    }

    public string GetDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return GetCanonicalDatabasePath(localAppData);
    }

    public string GetLogDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return GetCanonicalLogDirectory(localAppData);
    }

    public string GetAuthFilePath()
    {
        var home = this.GetUserProfileRoot();
        return GetCanonicalAuthFilePath(home);
    }

    public string GetPreferencesFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return GetCanonicalPreferencesPath(localAppData);
    }

    public string GetProviderConfigFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return GetCanonicalProviderConfigPath(localAppData);
    }

    public string GetMonitorInfoFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Join(GetCanonicalAppDataRoot(localAppData), "monitor.json");
    }

    public string GetUserProfileRoot()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string GetCanonicalAppDataRoot(string localAppDataRoot)
    {
        return Path.Join(localAppDataRoot, AppDirectoryName);
    }

    private static string GetCanonicalDatabasePath(string localAppDataRoot)
    {
        return Path.Join(GetCanonicalAppDataRoot(localAppDataRoot), "usage.db");
    }

    private static string GetCanonicalLogDirectory(string localAppDataRoot)
    {
        return Path.Join(GetCanonicalAppDataRoot(localAppDataRoot), "logs");
    }

    private static string GetCanonicalPreferencesPath(string localAppDataRoot)
    {
        return Path.Join(GetCanonicalAppDataRoot(localAppDataRoot), "preferences.json");
    }

    private static string GetCanonicalProviderConfigPath(string localAppDataRoot)
    {
        return Path.Join(GetCanonicalAppDataRoot(localAppDataRoot), "providers.json");
    }

    private static string GetCanonicalAuthFilePath(string userProfileRoot)
    {
        return Path.Join(userProfileRoot, ".opencode", "auth.json");
    }
}
