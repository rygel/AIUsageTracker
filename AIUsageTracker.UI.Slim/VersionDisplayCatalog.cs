// <copyright file="VersionDisplayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class VersionDisplayCatalog
{
    public static VersionDisplayPresentation Create(string versionCore, string? prereleaseLabel)
    {
        var displayVersion = string.IsNullOrWhiteSpace(prereleaseLabel)
            ? $"v{versionCore}"
            : $"v{versionCore} {prereleaseLabel}";

        return new VersionDisplayPresentation(
            DisplayVersion: displayVersion,
            WindowTitle: $"AI Usage Tracker {displayVersion}");
    }

    public static string? ParsePrereleaseLabel(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var normalized = informationalVersion.Split('+')[0];
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex < 0 || dashIndex >= normalized.Length - 1)
        {
            return null;
        }

        var suffix = normalized[(dashIndex + 1)..];
        if (suffix.StartsWith("beta.", StringComparison.OrdinalIgnoreCase))
        {
            var betaPart = suffix["beta.".Length..];
            return string.IsNullOrWhiteSpace(betaPart) ? "Beta" : $"Beta {betaPart}";
        }

        if (suffix.StartsWith("alpha.", StringComparison.OrdinalIgnoreCase))
        {
            var alphaPart = suffix["alpha.".Length..];
            return string.IsNullOrWhiteSpace(alphaPart) ? "Alpha" : $"Alpha {alphaPart}";
        }

        if (suffix.StartsWith("rc.", StringComparison.OrdinalIgnoreCase))
        {
            var rcPart = suffix["rc.".Length..];
            return string.IsNullOrWhiteSpace(rcPart) ? "RC" : $"RC {rcPart}";
        }

        return suffix.Replace('.', ' ');
    }
}
