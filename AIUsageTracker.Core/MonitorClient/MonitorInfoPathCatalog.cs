namespace AIUsageTracker.Core.MonitorClient;

public static class MonitorInfoPathCatalog
{
    public static IReadOnlyList<string> GetWriteCandidatePaths(string appDataRoot, string userProfileRoot)
    {
        return new[] { Path.Combine(appDataRoot, "AIUsageTracker", "monitor.json") };
    }

    public static IReadOnlyList<string> GetReadCandidatePaths(string appDataRoot, string userProfileRoot)
    {
        return new[] { Path.Combine(appDataRoot, "AIUsageTracker", "monitor.json") };
    }
}
