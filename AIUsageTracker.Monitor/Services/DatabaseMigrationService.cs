// <copyright file="DatabaseMigrationService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using EvolveDb;
using Microsoft.Data.Sqlite;

namespace AIUsageTracker.Monitor.Services;

public class DatabaseMigrationService
{
    private const string TableProviders = "providers";
    private const string TableProviderHistory = "provider_history";

    private static readonly Dictionary<string, string> TableInfoSql =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TableProviders] = "PRAGMA table_info(providers)",
            [TableProviderHistory] = "PRAGMA table_info(provider_history)",
            ["raw_snapshots"] = "PRAGMA table_info(raw_snapshots)",
            ["reset_events"] = "PRAGMA table_info(reset_events)",
        };

    private readonly string _connectionString;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(string dbPath, ILogger<DatabaseMigrationService> logger)
    {
        this._connectionString = $"Data Source={dbPath}";
        this._logger = logger;
    }

    public void RunMigrations()
    {
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            connection.Open();

            if (HasApplicationTables(connection) && !HasAppliedEvolveMigrations(connection))
            {
                this._logger.LogWarning(
                    "Existing database found without applied Evolve metadata. Applying compatibility bootstrap and skipping Evolve migrations.");
                this.EnsureSchemaCompatibility(connection);
                this.CleanupLegacyAnthropicRows(connection);
                this.ApplyPerformancePragmas(connection);
                return;
            }

            var evolve = new Evolve(connection, msg => this._logger.LogInformation("{Message}", msg))
            {
                EmbeddedResourceAssemblies = new[] { typeof(DatabaseMigrationService).Assembly },
                EmbeddedResourceFilters = new[] { "AIUsageTracker.Monitor.Migrations" },
            };

            try
            {
                evolve.Migrate();
            }
            catch (EvolveException ex) when (HasApplicationTables(connection) && IsExistingSchemaConflict(ex))
            {
                this._logger.LogWarning(
                    ex,
                    "Evolve migration conflicted with an existing schema ({Message}). Applying compatibility bootstrap instead.",
                    ex.Message);
                this.EnsureSchemaCompatibility(connection);
                this.CleanupLegacyAnthropicRows(connection);
                this.ApplyPerformancePragmas(connection);
                return;
            }

            this.CleanupLegacyAnthropicRows(connection);
            this.ApplyPerformancePragmas(connection);

            this._logger.LogInformation("DB migrated ({Count} applied)", evolve.NbMigration);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Database migration failed: {Message}", ex.Message);
            throw new InvalidOperationException("Database migration failed.", ex);
        }
    }

    private static bool HasApplicationTables(SqliteConnection connection)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND lower(name) IN ('providers', 'provider_history', 'raw_snapshots', 'reset_events')";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var count = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static bool HasAppliedEvolveMigrations(SqliteConnection connection)
    {
        const string sql = @"
            SELECT COUNT(*) FROM changelog";

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        try
        {
            var count = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            return count > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        if (!TableInfoSql.TryGetValue(tableName, out var pragmaSql))
        {
            throw new ArgumentException($"Table '{tableName}' is not in the migration allowlist.", nameof(tableName));
        }

        using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText = pragmaSql;

        var exists = false;
        using (var reader = infoCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        ExecuteNonQuery(connection, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};"); // sql-interpolation-allow — tableName validated against TableInfoSql allowlist; columnName/definition are hardcoded call-site constants
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool IsExistingSchemaConflict(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.InnerException!;
        }

        return false;
    }

    private static int ExecuteDelete(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private void EnsureSchemaCompatibility(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            @"
            CREATE TABLE IF NOT EXISTS providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                account_name TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                is_active INTEGER NOT NULL DEFAULT 1,
                config_json TEXT,
                auth_source TEXT DEFAULT 'manual'
            );

            CREATE TABLE IF NOT EXISTS provider_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                is_available INTEGER NOT NULL DEFAULT 1,
                status_message TEXT NOT NULL DEFAULT '',
                next_reset_time TEXT,
                requests_used REAL NOT NULL DEFAULT 0,
                requests_available REAL NOT NULL DEFAULT 0,
                requests_percentage REAL NOT NULL DEFAULT 0,
                response_latency_ms REAL NOT NULL DEFAULT 0,
                http_status INTEGER NOT NULL DEFAULT 0,
                upstream_response_validity INTEGER NOT NULL DEFAULT 0,
                upstream_response_note TEXT NOT NULL DEFAULT '',
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                details_json TEXT,
                parent_provider_id TEXT REFERENCES providers(provider_id) ON DELETE SET NULL,
                FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS raw_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL REFERENCES providers(provider_id) ON DELETE CASCADE,
                raw_json TEXT NOT NULL,
                http_status INTEGER NOT NULL DEFAULT 200,
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS reset_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                provider_name TEXT NOT NULL,
                previous_usage REAL,
                new_usage REAL,
                reset_type TEXT NOT NULL,
                timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
            );
        ");

        EnsureColumn(connection, TableProviders, "provider_name", "TEXT");
        EnsureColumn(connection, TableProviders, "account_name", "TEXT");
        EnsureColumn(connection, TableProviders, "created_at", "TEXT");
        EnsureColumn(connection, TableProviders, "updated_at", "TEXT");
        EnsureColumn(connection, TableProviders, "is_active", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, TableProviders, "config_json", "TEXT");
        EnsureColumn(connection, TableProviders, "auth_source", "TEXT DEFAULT 'manual'");
        EnsureColumn(connection, TableProviders, "plan_type", "TEXT DEFAULT 'usage'");
        EnsureColumn(connection, TableProviderHistory, "next_reset_time", "TEXT");
        EnsureColumn(connection, TableProviderHistory, "details_json", "TEXT");
        EnsureColumn(connection, TableProviderHistory, "next_reset_time", "TEXT");
        EnsureColumn(connection, TableProviderHistory, "response_latency_ms", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(connection, TableProviderHistory, "http_status", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, TableProviderHistory, "upstream_response_validity", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, TableProviderHistory, "upstream_response_note", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, TableProviderHistory, "parent_provider_id", "TEXT");
        EnsureColumn(connection, TableProviderHistory, "card_id", "TEXT");
        EnsureColumn(connection, TableProviderHistory, "group_id", "TEXT");
        EnsureColumn(connection, TableProviderHistory, "window_kind", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, TableProviderHistory, "model_name", "TEXT");
        EnsureColumn(connection, TableProviderHistory, "name", "TEXT");

        // Convert fetched_at TEXT → INTEGER epoch for databases that pre-date V11.
        ConvertTimestampsToEpochIfNeeded(connection);

        ExecuteNonQuery(
            connection,
            @"
            CREATE INDEX IF NOT EXISTS idx_history_provider_time ON provider_history(provider_id, fetched_at);
            CREATE INDEX IF NOT EXISTS idx_raw_fetched ON raw_snapshots(fetched_at);
            CREATE INDEX IF NOT EXISTS idx_reset_provider_time ON reset_events(provider_id, timestamp);
            CREATE INDEX IF NOT EXISTS idx_history_fetched_time ON provider_history(fetched_at DESC);
            CREATE INDEX IF NOT EXISTS idx_history_provider_id_desc ON provider_history(provider_id, id DESC);
            CREATE INDEX IF NOT EXISTS idx_history_is_available ON provider_history(is_available);
            CREATE INDEX IF NOT EXISTS idx_history_provider_fetched_desc ON provider_history(provider_id, fetched_at DESC);
        ");

        this._logger.LogInformation("Legacy database compatibility bootstrap completed.");
    }

    /// <summary>
    /// If <c>provider_history.fetched_at</c> is still stored as TEXT (ISO 8601), recreates
    /// both <c>provider_history</c> and <c>raw_snapshots</c> with INTEGER (Unix epoch seconds)
    /// storage.  Idempotent — no-op when the column is already INTEGER.
    /// This handles legacy databases that bypass EvolveDb and never ran V11.
    /// </summary>
    private static void ConvertTimestampsToEpochIfNeeded(SqliteConnection connection)
    {
        var type = GetColumnType(connection, TableProviderHistory, "fetched_at");
        if (string.IsNullOrEmpty(type) || !string.Equals(type, "TEXT", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ExecuteNonQuery(connection, "PRAGMA foreign_keys = OFF");
        ExecuteNonQuery(connection, @"
            CREATE TABLE provider_history_new (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                is_available INTEGER NOT NULL DEFAULT 1,
                status_message TEXT NOT NULL DEFAULT '',
                next_reset_time TEXT,
                requests_used REAL NOT NULL DEFAULT 0,
                requests_available REAL NOT NULL DEFAULT 0,
                requests_percentage REAL NOT NULL DEFAULT 0,
                response_latency_ms REAL NOT NULL DEFAULT 0,
                http_status INTEGER NOT NULL DEFAULT 0,
                upstream_response_validity INTEGER NOT NULL DEFAULT 0,
                upstream_response_note TEXT NOT NULL DEFAULT '',
                fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now')),
                details_json TEXT,
                parent_provider_id TEXT REFERENCES providers(provider_id) ON DELETE SET NULL,
                FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
            );
            INSERT INTO provider_history_new
            SELECT id, provider_id, is_available, status_message, next_reset_time,
                   requests_used, requests_available, requests_percentage,
                   response_latency_ms, http_status, upstream_response_validity, upstream_response_note,
                   CAST(strftime('%s', fetched_at) AS INTEGER),
                   details_json, parent_provider_id
            FROM provider_history;
            DROP TABLE provider_history;
            ALTER TABLE provider_history_new RENAME TO provider_history;

            CREATE TABLE raw_snapshots_new (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL REFERENCES providers(provider_id) ON DELETE CASCADE,
                raw_json TEXT NOT NULL,
                http_status INTEGER NOT NULL DEFAULT 200,
                fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
            );
            INSERT INTO raw_snapshots_new
            SELECT id, provider_id, raw_json, http_status,
                   CAST(strftime('%s', fetched_at) AS INTEGER)
            FROM raw_snapshots;
            DROP TABLE raw_snapshots;
            ALTER TABLE raw_snapshots_new RENAME TO raw_snapshots;
        ");
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON");
    }

    private static string? GetColumnType(SqliteConnection connection, string tableName, string columnName)
    {
        if (!TableInfoSql.TryGetValue(tableName, out var pragmaSql))
        {
            throw new ArgumentException($"Table '{tableName}' is not in the migration allowlist.", nameof(tableName));
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = pragmaSql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return reader["type"]?.ToString();
            }
        }

        return null;
    }

    private void CleanupLegacyAnthropicRows(SqliteConnection connection)
    {
        const string deleteProviderHistorySql = "DELETE FROM provider_history WHERE lower(provider_id) = 'anthropic';";
        const string deleteRawSnapshotsSql = "DELETE FROM raw_snapshots WHERE lower(provider_id) = 'anthropic';";
        const string deleteResetEventsSql = "DELETE FROM reset_events WHERE lower(provider_id) = 'anthropic';";
        const string deleteProvidersSql = "DELETE FROM providers WHERE lower(provider_id) = 'anthropic';";

        var deletedHistory = ExecuteDelete(connection, deleteProviderHistorySql);
        var deletedRawSnapshots = ExecuteDelete(connection, deleteRawSnapshotsSql);
        var deletedResetEvents = ExecuteDelete(connection, deleteResetEventsSql);
        var deletedProviders = ExecuteDelete(connection, deleteProvidersSql);

        if (deletedHistory + deletedRawSnapshots + deletedResetEvents + deletedProviders > 0)
        {
            this._logger.LogInformation(
                "Removed legacy anthropic rows from database. providers={Providers}, history={History}, snapshots={Snapshots}, resets={Resets}",
                deletedProviders,
                deletedHistory,
                deletedRawSnapshots,
                deletedResetEvents);
        }
    }

    private void ApplyPerformancePragmas(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            @"
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=5000;
            PRAGMA temp_store=MEMORY;
            PRAGMA foreign_keys=ON;
        ");

        this._logger.LogInformation("SQLite performance pragmas applied (WAL/NORMAL/shared read optimization).");
    }
}
