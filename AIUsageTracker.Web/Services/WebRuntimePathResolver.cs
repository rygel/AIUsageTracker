// <copyright file="WebRuntimePathResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Paths;

namespace AIUsageTracker.Web.Services;

internal static class WebRuntimePathResolver
{
    public static WebRuntimePaths Resolve(string localAppDataRoot)
    {
        var appRoot = AppPathCatalog.GetCanonicalAppDataRoot(localAppDataRoot);
        var logDirectory = AppPathCatalog.GetCanonicalLogDirectory(localAppDataRoot);
        var runtimeFallbackRoot = Path.Combine(AppContext.BaseDirectory, ".runtime");
        var writableAppRoot = EnsureWritableDirectory(appRoot, Path.Combine(runtimeFallbackRoot, "app-data"));
        var writableLogDirectory = EnsureWritableDirectory(logDirectory, Path.Combine(runtimeFallbackRoot, "logs"));
        var dataProtectionKeyDirectory = EnsureWritableDirectory(
            Path.Combine(writableAppRoot, "web-data-protection"),
            Path.Combine(runtimeFallbackRoot, "web-data-protection"));
        var databasePath = ResolveDatabasePath(localAppDataRoot, Path.Combine(runtimeFallbackRoot, "db-snapshot"));

        return new WebRuntimePaths(
            writableAppRoot,
            writableLogDirectory,
            dataProtectionKeyDirectory,
            databasePath);
    }

    public static string EnsureWritableDirectory(string preferredPath, string fallbackPath)
    {
        if (TryEnsureDirectory(preferredPath))
        {
            return preferredPath;
        }

        Directory.CreateDirectory(fallbackPath);
        return fallbackPath;
    }

    public static string ResolveDatabasePath(string localAppDataRoot, string snapshotRoot)
    {
        return WebDatabasePathResolver.Resolve(localAppDataRoot, snapshotRoot);
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal readonly record struct WebRuntimePaths(
        string AppRoot,
        string LogDirectory,
        string DataProtectionKeyDirectory,
        string DatabasePath);
}
