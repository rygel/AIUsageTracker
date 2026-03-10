// <copyright file="ConfigPathCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Core.Paths;

public static class ConfigPathCatalog
{
    public static IReadOnlyList<string> GetAuthConfigPaths(IAppPathProvider pathProvider)
    {
        return GetConfigEntries(pathProvider)
            .Where(entry => entry.Kind == ConfigPathKind.Auth)
            .Select(entry => entry.Path)
            .ToList();
    }

    public static IReadOnlyList<string> GetProviderConfigPaths(IAppPathProvider pathProvider)
    {
        return GetConfigEntries(pathProvider)
            .Where(entry => entry.Kind == ConfigPathKind.Provider)
            .Select(entry => entry.Path)
            .ToList();
    }

    public static IReadOnlyList<ConfigPathEntry> GetConfigEntries(IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        return new[]
        {
            new ConfigPathEntry(pathProvider.GetAuthFilePath(), true, ConfigPathKind.Auth),
            new ConfigPathEntry(pathProvider.GetProviderConfigFilePath(), false, ConfigPathKind.Provider),
        };
    }
}
