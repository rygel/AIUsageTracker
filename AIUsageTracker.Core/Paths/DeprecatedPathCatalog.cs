// <copyright file="DeprecatedPathCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Paths
{
    using System.IO;

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
                Path.Combine(GetLegacyLocalAppDataRoot(userProfileRoot), "AIConsumptionTracker", "providers.json"),
            };
        }

        // Deprecated paths are read-only migration fallbacks and should not be used for new writes.
        public static IReadOnlyList<string> GetPreferencesFilePaths(string userProfileRoot)
        {
            return new[]
            {
                Path.Combine(GetLegacyLocalAppDataRoot(userProfileRoot), "AIConsumptionTracker", "preferences.json"),
            };
        }

        private static string GetLegacyLocalAppDataRoot(string userProfileRoot)
        {
            return Path.Combine(userProfileRoot, "AppData", "Local");
        }
    }
}
