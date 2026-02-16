using Microsoft.Data.Sqlite;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Web.Services;

/// <summary>
/// Service for reading provider usage data from the Agent's SQLite database.
/// This is a read-only service that accesses the same database as the Agent.
/// </summary>
public class WebDatabaseService
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public WebDatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");
        _dbPath = Path.Combine(dbDir, "usage.db");
    }

    /// <summary>
    /// Check if database exists and is accessible
    /// </summary>
    public bool IsDatabaseAvailable()
    {
        return File.Exists(_dbPath);
    }

    /// <summary>
    /// Get the latest usage data for all providers
    /// </summary>
    public async Task<List<ProviderUsage>> GetLatestUsageAsync()
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT h.*, p.provider_name, p.payment_type
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.id IN (
                    SELECT MAX(id) FROM provider_history GROUP BY provider_id
                )
                ORDER BY p.provider_name";

            var results = new List<ProviderUsage>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(MapToProviderUsage(reader));
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
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT h.*, p.provider_name, p.payment_type
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

            var results = new List<ProviderUsage>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(MapToProviderUsage(reader));
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get history for a specific provider
    /// </summary>
    public async Task<List<ProviderUsage>> GetProviderHistoryAsync(string providerId, int limit = 100)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT h.*, p.provider_name, p.payment_type
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
                results.Add(MapToProviderUsage(reader));
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get all providers from the database
    /// </summary>
    public async Task<List<ProviderInfo>> GetProvidersAsync()
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderInfo>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT p.*, 
                    (SELECT usage_percentage FROM provider_history 
                     WHERE provider_id = p.provider_id 
                     ORDER BY fetched_at DESC LIMIT 1) as latest_usage
                FROM providers p
                WHERE p.is_active = 1
                ORDER BY p.provider_name";

            var results = new List<ProviderInfo>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new ProviderInfo
                {
                    ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
                    ProviderName = reader.GetString(reader.GetOrdinal("provider_name")),
                    PaymentType = reader.GetString(reader.GetOrdinal("payment_type")),
                    IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) == 1,
                    AuthSource = reader.IsDBNull(reader.GetOrdinal("auth_source")) ? null : reader.GetString(reader.GetOrdinal("auth_source")),
                    AccountName = reader.IsDBNull(reader.GetOrdinal("account_name")) ? null : reader.GetString(reader.GetOrdinal("account_name")),
                    LatestUsage = reader.IsDBNull(reader.GetOrdinal("latest_usage")) ? 0 : reader.GetDouble(reader.GetOrdinal("latest_usage"))
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
    /// Get reset events for a provider
    /// </summary>
    public async Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        if (!IsDatabaseAvailable())
            return new List<ResetEvent>();

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
    /// Get usage statistics summary
    /// </summary>
    public async Task<UsageSummary> GetUsageSummaryAsync()
    {
        if (!IsDatabaseAvailable())
            return new UsageSummary();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            // Get latest data for all providers
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    COUNT(DISTINCT provider_id) as provider_count,
                    AVG(usage_percentage) as avg_usage,
                    MAX(fetched_at) as last_update
                FROM provider_history
                WHERE id IN (
                    SELECT MAX(id) FROM provider_history GROUP BY provider_id
                )";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new UsageSummary
                {
                    ProviderCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    AverageUsage = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                    LastUpdate = reader.IsDBNull(2) ? null : reader.GetString(2)
                };
            }

            return new UsageSummary();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get time series data for charts (last N hours)
    /// </summary>
    public async Task<List<ChartDataPoint>> GetChartDataAsync(int hours = 24)
    {
        if (!IsDatabaseAvailable())
            return new List<ChartDataPoint>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    h.provider_id,
                    p.provider_name,
                    h.fetched_at,
                    h.usage_percentage,
                    h.cost_used
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE datetime(h.fetched_at) >= datetime('now', '-' || @hours || ' hours')
                ORDER BY h.fetched_at ASC";
            cmd.Parameters.AddWithValue("@hours", hours);

            var results = new List<ChartDataPoint>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new ChartDataPoint
                {
                    ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
                    ProviderName = reader.GetString(reader.GetOrdinal("provider_name")),
                    Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("fetched_at"))),
                    UsagePercentage = reader.GetDouble(reader.GetOrdinal("usage_percentage")),
                    CostUsed = reader.GetDouble(reader.GetOrdinal("cost_used"))
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
    /// Get raw providers table with pagination
    /// </summary>
    public async Task<(List<Dictionary<string, object?>> rows, int totalCount)> GetProvidersRawAsync(int page = 1, int pageSize = 100)
    {
        return await GetTableRawAsync("providers", page, pageSize);
    }

    /// <summary>
    /// Get raw provider_history table with pagination
    /// </summary>
    public async Task<(List<Dictionary<string, object?>> rows, int totalCount)> GetProviderHistoryRawAsync(int page = 1, int pageSize = 100)
    {
        return await GetTableRawAsync("provider_history", page, pageSize, "fetched_at DESC");
    }

    /// <summary>
    /// Get raw raw_snapshots table with pagination
    /// </summary>
    public async Task<(List<Dictionary<string, object?>> rows, int totalCount)> GetRawSnapshotsRawAsync(int page = 1, int pageSize = 100)
    {
        return await GetTableRawAsync("raw_snapshots", page, pageSize, "fetched_at DESC");
    }

    /// <summary>
    /// Get raw reset_events table with pagination
    /// </summary>
    public async Task<(List<Dictionary<string, object?>> rows, int totalCount)> GetResetEventsRawAsync(int page = 1, int pageSize = 100)
    {
        return await GetTableRawAsync("reset_events", page, pageSize, "timestamp DESC");
    }

    private async Task<(List<Dictionary<string, object?>> rows, int totalCount)> GetTableRawAsync(string tableName, int page, int pageSize, string? orderBy = null)
    {
        if (!IsDatabaseAvailable())
            return (new List<Dictionary<string, object?>>(), 0);

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            var offset = (page - 1) * pageSize;
            var orderClause = string.IsNullOrEmpty(orderBy) ? "" : $"ORDER BY {orderBy}";

            // Get total count
            int totalCount;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = $"SELECT COUNT(*) FROM {tableName}";
                totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }

            // Get rows
            var rows = new List<Dictionary<string, object?>>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM {tableName} {orderClause} LIMIT {pageSize} OFFSET {offset}";
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[reader.GetName(i)] = value;
                    }
                    rows.Add(row);
                }
            }

            return (rows, totalCount);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private ProviderUsage MapToProviderUsage(SqliteDataReader reader)
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
/// Provider information from the database
/// </summary>
public class ProviderInfo
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? AuthSource { get; set; }
    public string? AccountName { get; set; }
    public double LatestUsage { get; set; }
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

/// <summary>
/// Usage statistics summary
/// </summary>
public class UsageSummary
{
    public int ProviderCount { get; set; }
    public double AverageUsage { get; set; }
    public string? LastUpdate { get; set; }
}

/// <summary>
/// Chart data point for time series
/// </summary>
public class ChartDataPoint
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double UsagePercentage { get; set; }
    public double CostUsed { get; set; }
}
