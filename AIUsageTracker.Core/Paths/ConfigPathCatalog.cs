// <copyright file="ConfigPathCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Paths
{
    using AIUsageTracker.Core.Interfaces;

    public static class ConfigPathCatalog
    {
        public static IReadOnlyList<string> GetAuthConfigPaths(IAppPathProvider pathProvider)
        {
            return BuildPathList(
                pathProvider.GetAuthFilePath(),
                DeprecatedPathCatalog.GetAuthFilePaths(pathProvider.GetUserProfileRoot()));
        }

        public static IReadOnlyList<string> GetProviderConfigPaths(IAppPathProvider pathProvider)
        {
            return BuildPathList(
                pathProvider.GetProviderConfigFilePath(),
                DeprecatedPathCatalog.GetProviderConfigPaths(pathProvider.GetUserProfileRoot()));
        }

        private static IReadOnlyList<string> BuildPathList(string canonicalPath, IEnumerable<string> deprecatedPaths)
        {
            return new[] { canonicalPath }
                .Concat(deprecatedPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
