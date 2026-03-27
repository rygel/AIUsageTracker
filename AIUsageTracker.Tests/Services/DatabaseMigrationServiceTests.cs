// <copyright file="DatabaseMigrationServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

public sealed class DatabaseMigrationServiceTests : IDisposable
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly string _dbPath;

    public DatabaseMigrationServiceTests()
    {
        this._dbPath = TestTempPaths.CreateFilePath("ai-migration-tests", "migration.db");
    }

    [Fact]
    public void RunMigrations_LegacyDatabaseWithoutEvolveMetadata_AddsMissingProviderColumns()
    {
        this.CreateLegacySchemaWithoutEvolveMetadata();

        var service = new DatabaseMigrationService(this._dbPath, NullLogger<DatabaseMigrationService>.Instance);

        service.RunMigrations();

        var providerColumns = this.GetColumnNames("providers");
        Assert.Contains("created_at", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("updated_at", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("is_active", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("config_json", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("auth_source", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("plan_type", providerColumns, StringComparer.OrdinalIgnoreCase);

        var historyColumns = this.GetColumnNames("provider_history");
        Assert.Contains("http_status", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("upstream_response_validity", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("upstream_response_note", historyColumns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsageDatabase_StoreProviderAsync_WorksAfterLegacySchemaCompatibilityBootstrapAsync()
    {
        this.CreateLegacySchemaWithoutEvolveMetadata();

        var migrationService = new DatabaseMigrationService(this._dbPath, NullLogger<DatabaseMigrationService>.Instance);
        migrationService.RunMigrations();

        var pathProvider = new TestAppPathProvider(this._dbPath);
        var database = new UsageDatabase(NullLogger<UsageDatabase>.Instance, pathProvider);

        await database.StoreProviderAsync(
            new ProviderConfig
            {
                ProviderId = "antigravity",
                Type = "quota-based",
                AuthSource = "antigravity",
                ApiKey = TestApiKey,
            },
            friendlyName: "Google Antigravity");

        await using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT provider_name FROM providers WHERE provider_id = 'antigravity'";
        var name = (string?)await command.ExecuteScalarAsync();

        Assert.Equal("Google Antigravity", name);
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._dbPath);
    }

    private void CreateLegacySchemaWithoutEvolveMetadata()
    {
        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();

        const string sql = @"
            CREATE TABLE providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                auth_source TEXT DEFAULT 'manual'
            );

            CREATE TABLE provider_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                requests_used REAL NOT NULL DEFAULT 0,
                requests_available REAL NOT NULL DEFAULT 0,
                requests_percentage REAL NOT NULL DEFAULT 0,
                is_available INTEGER NOT NULL DEFAULT 1,
                status_message TEXT NOT NULL DEFAULT '',
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE raw_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                http_status INTEGER NOT NULL DEFAULT 200,
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE reset_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                provider_name TEXT NOT NULL,
                previous_usage REAL,
                new_usage REAL,
                reset_type TEXT NOT NULL,
                timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private HashSet<string> GetColumnNames(string tableName)
    {
        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var name = reader["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                columns.Add(name);
            }
        }

        return columns;
    }

    private sealed class TestAppPathProvider : IAppPathProvider
    {
        private readonly string _dbPath;

        public TestAppPathProvider(string dbPath)
        {
            this._dbPath = dbPath;
        }

        public string GetAppDataRoot() => Path.GetDirectoryName(this._dbPath)!;

        public string GetDatabasePath() => this._dbPath;

        public string GetLogDirectory() => Path.Combine(this.GetAppDataRoot(), "logs");

        public string GetAuthFilePath() => Path.Combine(this.GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this.GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this.GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => this.GetAppDataRoot();

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }
}
