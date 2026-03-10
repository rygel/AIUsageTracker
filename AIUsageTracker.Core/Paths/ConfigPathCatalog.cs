// <copyright file="ConfigPathCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Core.Paths;

public static class ConfigPathCatalog
{
    public static IReadOnlyList<ConfigPathEntry> GetConfigEntries(IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        return new[]
        {
            new ConfigPathEntry(pathProvider.GetAuthFilePath(), ConfigPathKind.Auth),
            new ConfigPathEntry(pathProvider.GetProviderConfigFilePath(), ConfigPathKind.Provider),
        };
    }
}
