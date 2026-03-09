// <copyright file="MonitorInfoPathCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient
{
    public static class MonitorInfoPathCatalog
    {
        private const string CanonicalProductFolder = "AIUsageTracker";
        private const string DeprecatedProductFolder = "AIConsumptionTracker";

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
                GetDeprecatedPath(appDataRoot),
            };
        }

        public static bool IsDeprecatedReadPath(string appDataRoot, string path)
        {
            return !string.Equals(
                path,
                GetCanonicalPath(appDataRoot),
                StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<string> GetReadCandidatePathsFromEnvironment()
        {
            var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return GetReadCandidatePaths(appDataRoot, userProfileRoot);
        }

        public static string? ResolveExistingReadPath()
        {
            return GetReadCandidatePathsFromEnvironment()
                .Where(File.Exists)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();
        }

        private static string GetCanonicalPath(string appDataRoot)
        {
            return Path.Combine(appDataRoot, CanonicalProductFolder, "monitor.json");
        }

        private static string GetDeprecatedPath(string appDataRoot)
        {
            return Path.Combine(appDataRoot, DeprecatedProductFolder, "monitor.json");
        }
    }
}
