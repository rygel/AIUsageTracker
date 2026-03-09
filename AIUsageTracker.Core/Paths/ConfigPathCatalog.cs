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
            return new[]
            {
                pathProvider.GetAuthFilePath(),
            };
        }

        public static IReadOnlyList<string> GetProviderConfigPaths(IAppPathProvider pathProvider)
        {
            return new[]
            {
                pathProvider.GetProviderConfigFilePath(),
            };
        }
    }
}
