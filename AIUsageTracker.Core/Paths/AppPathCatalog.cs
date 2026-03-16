// <copyright file="AppPathCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;

namespace AIUsageTracker.Core.Paths;

public static class AppPathCatalog
{
    private const string AppDirectoryName = "AIUsageTracker";

    public static string GetCanonicalAppDataRoot(string localAppDataRoot)
    {
        return Path.Combine(localAppDataRoot, AppDirectoryName);
    }

    public static string GetCanonicalDatabasePath(string localAppDataRoot)
    {
        return Path.Combine(GetCanonicalAppDataRoot(localAppDataRoot), "usage.db");
    }

    public static string GetCanonicalLogDirectory(string localAppDataRoot)
    {
        return Path.Combine(GetCanonicalAppDataRoot(localAppDataRoot), "logs");
    }

    public static string GetCanonicalPreferencesPath(string localAppDataRoot)
    {
        return Path.Combine(GetCanonicalAppDataRoot(localAppDataRoot), "preferences.json");
    }

    public static string GetCanonicalProviderConfigPath(string localAppDataRoot)
    {
        return Path.Combine(GetCanonicalAppDataRoot(localAppDataRoot), "providers.json");
    }

    public static string GetCanonicalAuthFilePath(string userProfileRoot)
    {
        return Path.Combine(userProfileRoot, ".opencode", "auth.json");
    }
}
