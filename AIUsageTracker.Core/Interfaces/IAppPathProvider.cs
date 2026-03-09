// <copyright file="IAppPathProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces
{
    public interface IAppPathProvider
    {
        string GetAppDataRoot();

        string GetDatabasePath();

        string GetLogDirectory();

        string GetAuthFilePath();

        string GetPreferencesFilePath();

        string GetProviderConfigFilePath();

        // Discovery root for external tools (e.g., .claude, .codex)
        string GetUserProfileRoot();
    }
}
