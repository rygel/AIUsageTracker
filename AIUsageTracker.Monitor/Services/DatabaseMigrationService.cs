using EvolveDb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public class DatabaseMigrationService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(string dbPath, ILogger<DatabaseMigrationService> logger)
    {
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public void RunMigrations()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            if (HasApplicationTables(connection) && !HasAppliedEvolveMigrations(connection))
            {
                _logger.LogWarning(
                    "Existing database found without applied Evolve metadata. Applying compatibility bootstrap and skipping Evolve migrations.");
                EnsureSchemaCompatibility(connection);
                ApplyPerformancePragmas(connection);
                return;
            }

            var evolve = new Evolve(connection, msg => _logger.LogInformation("{Message}", msg))
            {
                EmbeddedResourceAssemblies = new[] { typeof(DatabaseMigrationService).Assembly },
                EmbeddedResourceFilters = new[] { "AIUsageTracker.Monitor.Migrations" }
            };

            try
            {
                evolve.Migrate();
            }
            catch (EvolveException ex) when (HasApplicationTables(connection) && IsExistingSchemaConflict(ex))
            {
                _logger.LogWarning(
                    "Evolve migration conflicted with an existing schema ({Message}). Applying compatibility bootstrap instead.",
                    ex.Message);
                EnsureSchemaCompatibility(connection);
                ApplyPerformancePragmas(connection);
                return;
            }

            ApplyPerformancePragmas(connection);

            _logger.LogInformation("DB migrated ({Count} applied)", evolve.NbMigration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed: {Message}", ex.Message);
            throw;
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
        var count = Convert.ToInt32(command.ExecuteScalar());
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
            var count = Convert.ToInt32(command.ExecuteScalar());
            return count > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private void EnsureSchemaCompatibility(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, @"
            CREATE TABLE IF NOT EXISTS providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                account_name TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                is_active INTEGER NOT NULL DEFAULT 1,
                config_json TEXT,
                plan_type TEXT DEFAULT 'usage',
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
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                details_json TEXT,
                FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS raw_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
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

        EnsureColumn(connection, "providers", "plan_type", "TEXT DEFAULT 'usage'");
        EnsureColumn(connection, "providers", "auth_source", "TEXT DEFAULT 'manual'");
        EnsureColumn(connection, "provider_history", "details_json", "TEXT");

        ExecuteNonQuery(connection, @"
            CREATE INDEX IF NOT EXISTS idx_history_provider_time ON provider_history(provider_id, fetched_at);
            CREATE INDEX IF NOT EXISTS idx_raw_fetched ON raw_snapshots(fetched_at);
            CREATE INDEX IF NOT EXISTS idx_reset_provider_time ON reset_events(provider_id, timestamp);
            CREATE INDEX IF NOT EXISTS idx_history_fetched_time ON provider_history(fetched_at DESC);
            CREATE INDEX IF NOT EXISTS idx_history_provider_id_desc ON provider_history(provider_id, id DESC);
        ");

        _logger.LogInformation("Legacy database compatibility bootstrap completed.");
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText = $"PRAGMA table_info({tableName});";

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

        ExecuteNonQuery(connection, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};");
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

    private void ApplyPerformancePragmas(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, @"
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=5000;
            PRAGMA temp_store=MEMORY;
            PRAGMA foreign_keys=ON;
        ");

        _logger.LogInformation("SQLite performance pragmas applied (WAL/NORMAL/shared read optimization).");
    }
}


