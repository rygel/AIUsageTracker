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
        var entries = new List<ConfigPathEntry>
        {
            new(pathProvider.GetAuthFilePath(), ConfigPathKind.Auth),
            new(pathProvider.GetProviderConfigFilePath(), ConfigPathKind.Provider),
        };

        var appDataRoot = pathProvider.GetAppDataRoot();
        if (!string.IsNullOrWhiteSpace(appDataRoot))
        {
            entries.Add(new ConfigPathEntry(Path.Combine(appDataRoot, "auth.json"), ConfigPathKind.Auth));
        }
        var distinctEntries = new List<ConfigPathEntry>(entries.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            if (seenPaths.Add(entry.Path))
            {
                distinctEntries.Add(entry);
            }
        }

        return distinctEntries;
    }
}
