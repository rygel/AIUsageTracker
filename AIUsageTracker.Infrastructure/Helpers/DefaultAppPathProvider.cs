// <copyright file="DefaultAppPathProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Helpers
{
    using System.IO;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Paths;

    public class DefaultAppPathProvider : IAppPathProvider
    {
        public string GetAppDataRoot()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return AppPathCatalog.GetCanonicalAppDataRoot(localAppData);
        }

        public string GetDatabasePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return AppPathCatalog.GetCanonicalDatabasePath(localAppData);
        }

        public string GetLogDirectory()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return AppPathCatalog.GetCanonicalLogDirectory(localAppData);
        }

        public string GetAuthFilePath()
        {
            var home = this.GetUserProfileRoot();
            return AppPathCatalog.GetCanonicalAuthFilePath(home);
        }

        public string GetPreferencesFilePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return AppPathCatalog.GetCanonicalPreferencesPath(localAppData);
        }

        public string GetProviderConfigFilePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return AppPathCatalog.GetCanonicalProviderConfigPath(localAppData);
        }

        public string GetUserProfileRoot()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }
}
