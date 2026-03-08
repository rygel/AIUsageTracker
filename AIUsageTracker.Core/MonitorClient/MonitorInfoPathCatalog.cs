namespace AIUsageTracker.Core.MonitorClient;

public static class MonitorInfoPathCatalog
{
    // Legacy monitor.json locations are read-only migration fallbacks.
    // New writes must stay on the canonical AIUsageTracker path.
    public static IReadOnlyList<string> GetWriteCandidatePaths(string appDataRoot, string userProfileRoot)
    {
        return new[]
        {
            GetCanonicalPath(appDataRoot),
        };
    }

    public static IReadOnlyList<string> GetReadCandidatePaths(string appDataRoot, string userProfileRoot)
    {
        return new[]
        {
            GetCanonicalPath(appDataRoot),
            GetLegacyTrackerPath(appDataRoot, "AIUsageTracker"),
            GetLegacyTrackerPath(appDataRoot, "AIUsageTracker", "Monitor"),
            GetLegacyTrackerPath(appDataRoot, "AIUsageTracker", "Agent"),
            GetLegacyTrackerPath(appDataRoot, "AIConsumptionTracker"),
            GetLegacyTrackerPath(appDataRoot, "AIConsumptionTracker", "Monitor"),
            GetLegacyTrackerPath(appDataRoot, "AIConsumptionTracker", "Agent"),
        };
    }

    public static bool IsDeprecatedReadPath(string appDataRoot, string path)
    {
        return !string.Equals(
            path,
            GetCanonicalPath(appDataRoot),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCanonicalPath(string appDataRoot)
    {
        return Path.Combine(appDataRoot, "AIUsageTracker", "monitor.json");
    }

    private static string GetLegacyTrackerPath(string appDataRoot, string productFolder, string? subfolder = null)
    {
        return subfolder == null
            ? Path.Combine(appDataRoot, productFolder, "monitor.json")
            : Path.Combine(appDataRoot, productFolder, subfolder, "monitor.json");
    }
}
