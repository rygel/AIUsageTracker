// <copyright file="WebDatabasePathResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Microsoft.Data.Sqlite;
using Serilog;

namespace AIUsageTracker.Web.Services;

internal static class WebDatabasePathResolver
{
    private const string AppDirectoryName = "AIUsageTracker";

    public static string Resolve(string localAppDataRoot, string snapshotRoot)
    {
        var canonicalDatabasePath = GetCanonicalDatabasePath(localAppDataRoot);
        var snapshotDatabasePath = Path.Combine(snapshotRoot, "usage.db");
        if (TryCopySnapshot(canonicalDatabasePath, snapshotDatabasePath) && CanOpen(snapshotDatabasePath))
        {
            Log.Information(
                "Using runtime snapshot database for web UI. Source: {SourcePath}; Snapshot: {SnapshotPath}",
                canonicalDatabasePath,
                snapshotDatabasePath);
            return snapshotDatabasePath;
        }

        return canonicalDatabasePath;
    }

    public static bool CanOpen(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
                DefaultTimeout = 5,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master LIMIT 1";
            _ = command.ExecuteScalar();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Web UI could not open SQLite database at {DatabasePath}", databasePath);
            return false;
        }
    }

    private static bool TryCopySnapshot(string sourceDatabasePath, string destinationDatabasePath)
    {
        try
        {
            var destinationDirectory = Path.GetDirectoryName(destinationDatabasePath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                return false;
            }

            Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourceDatabasePath, destinationDatabasePath, overwrite: true);

            foreach (var sidecarSuffix in new[] { "-wal", "-shm" })
            {
                var sourceSidecarPath = sourceDatabasePath + sidecarSuffix;
                var destinationSidecarPath = destinationDatabasePath + sidecarSuffix;
                if (!File.Exists(sourceSidecarPath))
                {
                    File.Delete(destinationSidecarPath);
                    continue;
                }

                File.Copy(sourceSidecarPath, destinationSidecarPath, overwrite: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to create runtime database snapshot for web UI from {SourcePath} to {DestinationPath}",
                sourceDatabasePath,
                destinationDatabasePath);
            return false;
        }
    }

    private static string GetCanonicalDatabasePath(string localAppDataRoot)
    {
        return Path.Combine(localAppDataRoot, AppDirectoryName, "usage.db");
    }
}
