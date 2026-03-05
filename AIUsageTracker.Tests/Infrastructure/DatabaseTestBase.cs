using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using AIUsageTracker.Web.Services;
using AIUsageTracker.Core.Interfaces;
using Moq;
using System.Data;

namespace AIUsageTracker.Tests.Infrastructure;

public abstract class DatabaseTestBase : IDisposable
{
    private readonly SqliteConnection _sharedConnection;
    protected string DbPath { get; }
    protected string ConnectionString { get; }
    protected IMemoryCache Cache { get; }
    protected WebDatabaseService DatabaseService { get; }

    protected DatabaseTestBase()
    {
        // Use a real file for WebDatabaseService because it creates its own connections
        DbPath = Path.Combine(Path.GetTempPath(), $"ai-tracker-test-{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={DbPath}";
        
        // Ensure directory exists
        var dir = Path.GetDirectoryName(DbPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Keep one connection open to ensure the file exists and is available
        _sharedConnection = new SqliteConnection(ConnectionString);
        _sharedConnection.Open();

        Cache = new MemoryCache(new MemoryCacheOptions());
        InitializeSchema();

        DatabaseService = new WebDatabaseService(Cache, NullLogger<WebDatabaseService>.Instance, DbPath);
    }

    private void InitializeSchema()
    {
        const string schema = @"
            CREATE TABLE providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                account_name TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                is_active INTEGER NOT NULL DEFAULT 1,
                config_json TEXT,
                auth_source TEXT DEFAULT 'manual'
            );

            CREATE TABLE provider_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                is_available INTEGER NOT NULL DEFAULT 1,
                status_message TEXT NOT NULL DEFAULT '',
                next_reset_time TEXT,
                requests_used REAL NOT NULL DEFAULT 0,
                requests_available REAL NOT NULL DEFAULT 0,
                requests_percentage REAL NOT NULL DEFAULT 0,
                response_latency_ms REAL NOT NULL DEFAULT 0,
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                details_json TEXT,
                FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
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
                timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
            );";

        using var command = _sharedConnection.CreateCommand();
        command.CommandText = schema;
        command.ExecuteNonQuery();
    }

    protected void SeedProvider(string id, string name, string? account = null, bool isActive = true)
    {
        using var command = _sharedConnection.CreateCommand();
        command.CommandText = "INSERT INTO providers (provider_id, provider_name, account_name, is_active) VALUES ($id, $name, $account, $active)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$account", (object?)account ?? DBNull.Value);
        command.Parameters.AddWithValue("$active", isActive ? 1 : 0);
        command.ExecuteNonQuery();
    }

    protected void SeedHistory(string providerId, double used, double available, DateTime fetchedAt, bool isAvailable = true, double latencyMs = 0)
    {
        using var command = _sharedConnection.CreateCommand();
        command.CommandText = @"
            INSERT INTO provider_history (
                provider_id, requests_used, requests_available, requests_percentage, fetched_at, is_available, response_latency_ms
            ) VALUES (
                $id, $used, $available, $pct, $at, $avail, $latency
            )";
        
        var pct = available > 0 ? (1.0 - (used / available)) * 100.0 : 0;
        
        command.Parameters.AddWithValue("$id", providerId);
        command.Parameters.AddWithValue("$used", used);
        command.Parameters.AddWithValue("$available", available);
        command.Parameters.AddWithValue("$pct", pct);
        command.Parameters.AddWithValue("$at", fetchedAt.ToString("yyyy-MM-dd HH:mm:ss")); // Consistent format
        command.Parameters.AddWithValue("$avail", isAvailable ? 1 : 0);
        command.Parameters.AddWithValue("$latency", latencyMs);
        command.ExecuteNonQuery();
    }

    public virtual void Dispose()
    {
        _sharedConnection.Close();
        _sharedConnection.Dispose();
        Cache.Dispose();
        try
        {
            if (File.Exists(DbPath))
            {
                File.Delete(DbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
