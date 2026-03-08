using System.IO;

namespace AIUsageTracker.Core.Paths;

public static class DeprecatedPathCatalog
{
    // Deprecated paths are read-only migration fallbacks and should not be used for new writes.
    public static IReadOnlyList<string> GetAuthFilePaths(string userProfileRoot)
    {
        return new[]
        {
            Path.Combine(userProfileRoot, ".ai-consumption-tracker", "auth.json"),
        };
    }

    // Deprecated paths are read-only migration fallbacks and should not be used for new writes.
    public static IReadOnlyList<string> GetProviderConfigPaths(string userProfileRoot)
    {
        return new[]
        {
            Path.Combine(GetLocalAppDataRoot(userProfileRoot), "AIConsumptionTracker", "providers.json"),
        };
    }

    // Deprecated paths are read-only migration fallbacks and should not be used for new writes.
    public static IReadOnlyList<string> GetPreferencesFilePaths(string userProfileRoot)
    {
        return new[]
        {
            Path.Combine(GetLocalAppDataRoot(userProfileRoot), "AIConsumptionTracker", "preferences.json"),
        };
    }

    private static string GetLocalAppDataRoot(string userProfileRoot)
    {
        return Path.Combine(userProfileRoot, "AppData", "Local");
    }
}
