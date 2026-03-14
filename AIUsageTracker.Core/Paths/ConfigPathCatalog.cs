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

        var entries = new List<ConfigPathEntry>();
        var userProfileRoot = pathProvider.GetUserProfileRoot();
        foreach (var legacyAuthPath in GetLegacyOpenCodeAuthPaths(userProfileRoot))
        {
            entries.Add(new ConfigPathEntry(legacyAuthPath, ConfigPathKind.Auth));
        }

        entries.Add(new ConfigPathEntry(pathProvider.GetProviderConfigFilePath(), ConfigPathKind.Provider));

        var appDataRoot = pathProvider.GetAppDataRoot();
        if (!string.IsNullOrWhiteSpace(appDataRoot))
        {
            entries.Add(new ConfigPathEntry(Path.Combine(appDataRoot, "auth.json"), ConfigPathKind.Auth));
        }

        // Canonical app auth file is read last so explicit user-entered keys remain authoritative.
        entries.Add(new ConfigPathEntry(pathProvider.GetAuthFilePath(), ConfigPathKind.Auth));

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

    private static IEnumerable<string> GetLegacyOpenCodeAuthPaths(string? userProfileRoot)
    {
        if (string.IsNullOrWhiteSpace(userProfileRoot))
        {
            yield break;
        }

        // Ordered least-authoritative to most-authoritative (later entries win).
        // ~/.opencode/ is a legacy path with potentially stale keys.
        // ~/.local/share/opencode/ is the active XDG data directory maintained by OpenCode.
        yield return Path.Combine(userProfileRoot, ".opencode", "auth.json");
        yield return Path.Combine(userProfileRoot, ".config", "opencode", "auth.json");
        yield return Path.Combine(userProfileRoot, "AppData", "Roaming", "opencode", "auth.json");
        yield return Path.Combine(userProfileRoot, "AppData", "Local", "opencode", "auth.json");
        yield return Path.Combine(userProfileRoot, ".local", "share", "opencode", "auth.json");
    }
}
