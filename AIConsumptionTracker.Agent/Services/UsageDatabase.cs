using System.Text.Json;
using AIConsumptionTracker.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Agent.Services;

/// <summary>
/// Database service for storing provider usage data, raw snapshots, and reset events.
/// Implements a four-table design:
/// 1. providers - Static provider configuration
/// 2. provider_history - Time-series usage data (kept indefinitely)
/// 3. raw_snapshots - Raw JSON data (14-day TTL)
/// 4. reset_events - Quota/limit reset tracking (kept indefinitely)
/// </summary>
public class UsageDatabase
{
    private readonly string _dbPath;
    private readonly ILogger<UsageDatabase> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public UsageDatabase(ILogger<UsageDatabase> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");
        Directory.CreateDirectory(dbDir);
        _dbPath = Path.Combine(dbDir, "usage.db");
        InitializeDatabase();
    }

    /// <summary>
    /// Initialize all database tables and indexes
    /// </summary>
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        // Table 1: providers - Static provider configuration
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS providers (
                    provider_id TEXT PRIMARY KEY,
                    provider_name TEXT NOT NULL,
                    payment_type TEXT NOT NULL,
                    api_key TEXT,
                    base_url TEXT,
                    auth_source TEXT NOT NULL DEFAULT 'manual',
                    account_name TEXT,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    config_json TEXT
                )";
            cmd.ExecuteNonQuery();
        }

        // Table 2: provider_history - Time-series usage data (kept indefinitely)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS provider_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider_id TEXT NOT NULL,
                    usage_percentage REAL NOT NULL,
                    cost_used REAL NOT NULL,
                    cost_limit REAL NOT NULL,
                    is_available INTEGER NOT NULL DEFAULT 1,
                    status_message TEXT NOT NULL DEFAULT '',
                    next_reset_time TEXT,
                    fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
                )";
            cmd.ExecuteNonQuery();
        }

        // Index for provider_history
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_history_provider_time 
                ON provider_history(provider_id, fetched_at)";
            cmd.ExecuteNonQuery();
        }

        // Table 3: raw_snapshots - Raw JSON data (14-day TTL)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS raw_snapshots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider_id TEXT NOT NULL,
                    raw_json TEXT NOT NULL,
                    http_status INTEGER NOT NULL DEFAULT 200,
                    fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                )";
            cmd.ExecuteNonQuery();
        }

        // Index for raw_snapshots
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_raw_fetched 
                ON raw_snapshots(fetched_at)";
            cmd.ExecuteNonQuery();
        }

        // Table 4: reset_events - Quota/limit reset tracking (kept indefinitely)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS reset_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider_id TEXT NOT NULL,
                    provider_name TEXT NOT NULL,
                    previous_usage REAL,
                    new_usage REAL,
                    reset_type TEXT NOT NULL,
                    timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
                )";
            cmd.ExecuteNonQuery();
        }

        // Index for reset_events
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_reset_provider_time 
                ON reset_events(provider_id, timestamp)";
            cmd.ExecuteNonQuery();
        }

        _logger.LogInformation("Database initialized with four tables: providers, provider_history, raw_snapshots, reset_events");
    }

    /// <summary>
    /// Store provider configuration. Creates or updates provider record.
    /// </summary>
    public async Task StoreProviderAsync(ProviderConfig config)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO providers (
                    provider_id, provider_name, payment_type, api_key, base_url,
                    auth_source, account_name, updated_at, is_active, config_json
                ) VALUES (
                    @provider_id, @provider_name, @payment_type, @api_key, @base_url,
                    @auth_source, @account_name, CURRENT_TIMESTAMP, @is_active, @config_json
                )";

            cmd.Parameters.AddWithValue("@provider_id", config.ProviderId);
            cmd.Parameters.AddWithValue("@provider_name", config.ProviderId); // Use ID as name for now
            cmd.Parameters.AddWithValue("@payment_type", config.Type ?? "unknown");
            cmd.Parameters.AddWithValue("@api_key", config.ApiKey ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@base_url", config.BaseUrl ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@auth_source", config.AuthSource ?? "manual");
            cmd.Parameters.AddWithValue("@account_name", (object)DBNull.Value); // No account name in config
            cmd.Parameters.AddWithValue("@is_active", !string.IsNullOrEmpty(config.ApiKey) ? 1 : 0);
            cmd.Parameters.AddWithValue("@config_json", JsonSerializer.Serialize(config) ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogDebug("Stored provider configuration: {ProviderId}", config.ProviderId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Store usage history data (indefinite retention)
    /// </summary>
    public async Task StoreHistoryAsync(IEnumerable<ProviderUsage> usages)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            foreach (var usage in usages)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO provider_history (
                        provider_id, usage_percentage, cost_used, cost_limit,
                        is_available, status_message, next_reset_time, fetched_at
                    ) VALUES (
                        @provider_id, @usage_percentage, @cost_used, @cost_limit,
                        @is_available, @status_message, @next_reset_time, @fetched_at
                    )";

                cmd.Parameters.AddWithValue("@provider_id", usage.ProviderId);
                cmd.Parameters.AddWithValue("@usage_percentage", usage.UsagePercentage);
                cmd.Parameters.AddWithValue("@cost_used", usage.CostUsed);
                cmd.Parameters.AddWithValue("@cost_limit", usage.CostLimit);
                cmd.Parameters.AddWithValue("@is_available", usage.IsAvailable ? 1 : 0);
                cmd.Parameters.AddWithValue("@status_message", usage.Description ?? "");
                cmd.Parameters.AddWithValue("@next_reset_time", usage.NextResetTime?.ToString("O") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@fetched_at", usage.FetchedAt == default ? DateTime.UtcNow.ToString("O") : usage.FetchedAt.ToString("O"));

                await cmd.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("Stored {Count} history records", usages.Count());
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Store raw JSON snapshot (auto-deleted after 14 days)
    /// </summary>
    public async Task StoreRawSnapshotAsync(string providerId, string rawJson, int httpStatus)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO raw_snapshots (provider_id, raw_json, http_status, fetched_at)
                VALUES (@provider_id, @raw_json, @http_status, CURRENT_TIMESTAMP)";

            cmd.Parameters.AddWithValue("@provider_id", providerId);
            cmd.Parameters.AddWithValue("@raw_json", rawJson);
            cmd.Parameters.AddWithValue("@http_status", httpStatus);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogDebug("Stored raw snapshot for {ProviderId}", providerId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Delete old raw snapshots (14-day retention)
    /// </summary>
    public async Task CleanupOldSnapshotsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM raw_snapshots 
                WHERE fetched_at < datetime('now', '-14 days')";

            var deletedCount = await cmd.ExecuteNonQueryAsync();
            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old raw snapshots", deletedCount);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Store a reset event (indefinite retention)
    /// </summary>
    public async Task StoreResetEventAsync(string providerId, string providerName, 
        double? previousUsage, double? newUsage, string resetType)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO reset_events (
                    provider_id, provider_name, previous_usage, new_usage, reset_type, timestamp
                ) VALUES (
                    @provider_id, @provider_name, @previous_usage, @new_usage, @reset_type, CURRENT_TIMESTAMP
                )";

            cmd.Parameters.AddWithValue("@provider_id", providerId);
            cmd.Parameters.AddWithValue("@provider_name", providerName);
            cmd.Parameters.AddWithValue("@previous_usage", previousUsage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@new_usage", newUsage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@reset_type", resetType);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Stored reset event for {ProviderId}: {ResetType}", providerId, resetType);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get latest history for all providers
    /// </summary>
    public async Task<List<ProviderUsage>> GetLatestHistoryAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT h.*, p.provider_name 
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.id IN (
                    SELECT MAX(id) FROM provider_history GROUP BY provider_id
                )
                AND h.provider_id != 'antigravity'
                ORDER BY p.provider_name";

            var results = new List<ProviderUsage>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(MapHistoryToProviderUsage(reader));
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get historical data for all providers
    /// </summary>
    public async Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT h.*, p.provider_name 
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.provider_id != 'antigravity'
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

            var results = new List<ProviderUsage>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(MapHistoryToProviderUsage(reader));
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get history for specific provider
    /// </summary>
    public async Task<List<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT h.*, p.provider_name 
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.provider_id = @providerId
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";
            cmd.Parameters.AddWithValue("@providerId", providerId);

            var results = new List<ProviderUsage>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(MapHistoryToProviderUsage(reader));
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get reset events for a provider
    /// </summary>
    public async Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT * FROM reset_events
                WHERE provider_id = @providerId
                ORDER BY timestamp DESC
                LIMIT {limit}";
            cmd.Parameters.AddWithValue("@providerId", providerId);

            var results = new List<ResetEvent>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new ResetEvent
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")).ToString(),
                    ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
                    ProviderName = reader.GetString(reader.GetOrdinal("provider_name")),
                    PreviousUsage = reader.IsDBNull(reader.GetOrdinal("previous_usage")) ? null : reader.GetDouble(reader.GetOrdinal("previous_usage")),
                    NewUsage = reader.IsDBNull(reader.GetOrdinal("new_usage")) ? null : reader.GetDouble(reader.GetOrdinal("new_usage")),
                    ResetType = reader.GetString(reader.GetOrdinal("reset_type")),
                    Timestamp = reader.GetString(reader.GetOrdinal("timestamp"))
                });
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Map database row to ProviderUsage object
    /// </summary>
    private ProviderUsage MapHistoryToProviderUsage(SqliteDataReader reader)
    {
        return new ProviderUsage
        {
            ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
            ProviderName = reader.GetString(reader.GetOrdinal("provider_name")),
            UsagePercentage = reader.GetDouble(reader.GetOrdinal("usage_percentage")),
            CostUsed = reader.GetDouble(reader.GetOrdinal("cost_used")),
            CostLimit = reader.GetDouble(reader.GetOrdinal("cost_limit")),
            IsAvailable = reader.GetInt32(reader.GetOrdinal("is_available")) == 1,
            Description = reader.GetString(reader.GetOrdinal("status_message")),
            FetchedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("fetched_at"))),
            NextResetTime = reader.IsDBNull(reader.GetOrdinal("next_reset_time")) 
                ? null 
                : DateTime.Parse(reader.GetString(reader.GetOrdinal("next_reset_time")))
        };
    }
}

/// <summary>
/// Represents a quota/limit reset event
/// </summary>
public class ResetEvent
{
    public string Id { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public double? PreviousUsage { get; set; }
    public double? NewUsage { get; set; }
    public string ResetType { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}
