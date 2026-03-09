// <copyright file="ReleaseUrlCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Updates
{
    public static class ReleaseUrlCatalog
    {
        private const string RepositoryBaseUrl = "https://github.com/rygel/AIConsumptionTracker";

        public static string GetReleasesPageUrl()
        {
            return $"{RepositoryBaseUrl}/releases";
        }

        public static string GetLatestReleasePageUrl()
        {
            return $"{GetReleasesPageUrl()}/latest";
        }

        public static string GetReleaseTagUrl(string version)
        {
            return $"{GetReleasesPageUrl()}/tag/v{version}";
        }

        public static string GetGitHubReleaseApiUrl(string version)
        {
            return $"https://api.github.com/repos/rygel/AIConsumptionTracker/releases/tags/v{version}";
        }

        public static string GetAppcastUrl(string architecture, bool isBeta)
        {
            var normalizedArchitecture = architecture.ToLowerInvariant() switch
            {
                "arm" => "arm64",
                "arm64" => "arm64",
                "x86" => "x86",
                _ => "x64",
            };

            var appcastName = isBeta
                ? $"appcast_beta_{normalizedArchitecture}.xml"
                : $"appcast_{normalizedArchitecture}.xml";

            return $"{GetReleasesPageUrl()}/latest/download/{appcastName}";
        }
    }
}
