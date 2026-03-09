using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

public sealed class DatabaseMigrationServiceTests : IDisposable
{
    private readonly string _dbPath;

    public DatabaseMigrationServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ai-migration-tests-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public void RunMigrations_LegacyDatabaseWithoutEvolveMetadata_AddsMissingProviderColumns()
    {
        CreateLegacySchemaWithoutEvolveMetadata();

        var service = new DatabaseMigrationService(_dbPath, NullLogger<DatabaseMigrationService>.Instance);

        service.RunMigrations();

        var providerColumns = GetColumnNames("providers");
        Assert.Contains("created_at", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("updated_at", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("is_active", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("config_json", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("auth_source", providerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("plan_type", providerColumns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsageDatabase_StoreProviderAsync_WorksAfterLegacySchemaCompatibilityBootstrap()
    {
        CreateLegacySchemaWithoutEvolveMetadata();

        var migrationService = new DatabaseMigrationService(_dbPath, NullLogger<DatabaseMigrationService>.Instance);
        migrationService.RunMigrations();

        var pathProvider = new TestAppPathProvider(_dbPath);
        var database = new UsageDatabase(NullLogger<UsageDatabase>.Instance, pathProvider);

        await database.StoreProviderAsync(new ProviderConfig
        {
            ProviderId = "antigravity",
            Type = "quota-based",
            AuthSource = "antigravity",
            ApiKey = "dynamic"
        }, friendlyName: "Google Antigravity");

        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT provider_name FROM providers WHERE provider_id = 'antigravity'";
        var name = (string?)await command.ExecuteScalarAsync();

        Assert.Equal("Google Antigravity", name);
    }
`n
    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Ignore cleanup failures for temp db files.
        }
    }
`n
    private void CreateLegacySchemaWithoutEvolveMetadata()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
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
`n
    private HashSet<string> GetColumnNames(string tableName)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
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
`n
    private sealed class TestAppPathProvider : IAppPathProvider
    {
        private readonly string _dbPath;

        public TestAppPathProvider(string dbPath)
        {
            _dbPath = dbPath;
        }
`n
        public string GetAppDataRoot() => Path.GetDirectoryName(_dbPath)!;

        public string GetDatabasePath() => _dbPath;

        public string GetLogDirectory() => Path.Combine(GetAppDataRoot(), "logs");

        public string GetAuthFilePath() => Path.Combine(GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => GetAppDataRoot();
    }
}
