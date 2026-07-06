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
        Assert.Contains("parent_provider_id", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("card_id", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("group_id", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("window_kind", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("model_name", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("name", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("card_type", historyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("reset_credits_available", historyColumns, StringComparer.OrdinalIgnoreCase);
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

    [Fact]
    public void RunMigrations_LegacyDatabase_CardTypeQueryWorksAfterMigration()
    {
        this.CreateLegacySchemaWithoutEvolveMetadata();

        var service = new DatabaseMigrationService(this._dbPath, NullLogger<DatabaseMigrationService>.Instance);
        service.RunMigrations();

        // Verify that queries referencing card_type don't crash — this is the exact
        // query pattern that broke beta 9 on pre-Evolve databases.
        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(card_type, 'quota') FROM provider_history LIMIT 1";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void RunMigrations_LegacyDatabase_TimestampConversionPreservesCardColumns()
    {
        // Insert rows WITH card data, then run migration, then verify the data survived.
        // This catches the bug where ConvertTimestampsToEpochIfNeeded recreated the table
        // without copying card_type/window_kind/card_id columns.
        this.CreateLegacySchemaWithCardData();

        var service = new DatabaseMigrationService(this._dbPath, NullLogger<DatabaseMigrationService>.Instance);
        service.RunMigrations();

        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT card_id, group_id, window_kind, model_name, name, card_type
            FROM provider_history WHERE provider_id = 'openai' ORDER BY id";

        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("burst", reader.GetString(0));
        Assert.Equal("rolling", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal("gpt-4", reader.GetString(3));
        Assert.Equal("GPT-4", reader.GetString(4));
        Assert.Equal("windowed", reader.GetString(5));
    }

    [Fact]
    public void RunMigrations_LegacyTableWithUnknownColumn_ThrowsInsteadOfSilentlyDropping()
    {
        // If the source table has a column the conversion's CREATE TABLE doesn't know about,
        // the migration MUST throw — never silently drop data.
        this.CreateLegacySchemaWithUnknownColumn();

        var service = new DatabaseMigrationService(this._dbPath, NullLogger<DatabaseMigrationService>.Instance);

        // The INSERT will fail because provider_history_new has no column "future_column".
        Assert.ThrowsAny<Exception>(() => service.RunMigrations());
    }

    private void CreateLegacySchemaWithUnknownColumn()
    {
        // Simulates a database with a column the migration code doesn't know about.
        // The migration MUST throw, not silently drop it.
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
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                future_column TEXT
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
            );

            INSERT INTO provider_history (provider_id, future_column)
            VALUES ('test', 'precious data');";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._dbPath);
    }

    private void CreateLegacySchemaWithCardData()
    {
        // Simulates a database from a version that already had card columns
        // but still uses TEXT timestamps (pre-epoch-conversion).
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
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                card_id TEXT,
                group_id TEXT,
                window_kind INTEGER NOT NULL DEFAULT 0,
                model_name TEXT,
                name TEXT,
                card_type TEXT
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
            );

            INSERT INTO providers (provider_id, provider_name)
            VALUES ('openai', 'OpenAI');

            INSERT INTO provider_history
                (provider_id, requests_used, requests_available, requests_percentage,
                 is_available, status_message, fetched_at,
                 card_id, group_id, window_kind, model_name, name, card_type)
            VALUES
                ('openai', 100, 200, 50, 1, 'OK', '2024-01-15 10:30:00',
                 'burst', 'rolling', 1, 'gpt-4', 'GPT-4', 'windowed');";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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
