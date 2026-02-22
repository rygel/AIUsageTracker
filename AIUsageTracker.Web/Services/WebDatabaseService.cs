using Dapper;
using Microsoft.Data.Sqlite;
using AIUsageTracker.Core.Models;
using System.Globalization;
using System.Text.Json;

namespace AIUsageTracker.Web.Services;

public class WebDatabaseService
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private static int _chartIndexesEnsured;

    public WebDatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = ResolveDatabaseDirectory(appData);
        _dbPath = Path.Combine(dbDir, "usage.db");
    }

    private static string ResolveDatabaseDirectory(string appData)
    {
        var primaryDir = Path.Combine(appData, "AIUsageTracker", "Agent");
        var legacyDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");

        var primaryDb = Path.Combine(primaryDir, "usage.db");
        var legacyDb = Path.Combine(legacyDir, "usage.db");

        if (File.Exists(primaryDb))
        {
            return primaryDir;
        }

        if (File.Exists(legacyDb))
        {
            return legacyDir;
        }

        return primaryDir;
    }

    public bool IsDatabaseAvailable()
    {
        return File.Exists(_dbPath);
    }

    public async Task<List<ProviderUsage>> GetLatestUsageAsync()
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                       h.status_message AS Description, h.fetched_at AS FetchedAt,
                       h.next_reset_time AS NextResetTime, h.details_json AS DetailsJson
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.id IN (
                    SELECT MAX(id) FROM provider_history GROUP BY provider_id
                )
                ORDER BY p.provider_name";

            var results = await connection.QueryAsync<ProviderUsage>(sql);
            
            // Deserialize details from JSON
            foreach (var usage in results)
            {
                if (!string.IsNullOrEmpty(usage.DetailsJson))
                {
                    try
                    {
                        usage.Details = JsonSerializer.Deserialize<List<ProviderUsageDetail>>(usage.DetailsJson);
                    }
                    catch { /* Ignore deserialization errors */ }
                }
            }
            
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            var sql = $@"
                SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                       h.status_message AS Description, h.fetched_at AS FetchedAt,
                       h.next_reset_time AS NextResetTime, p.plan_type AS PlanType
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

            var results = await connection.QueryAsync<ProviderUsage>(sql);
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetProviderHistoryAsync(string providerId, int limit = 100)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            var sql = $@"
                SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                       h.status_message AS Description, h.fetched_at AS FetchedAt,
                       h.next_reset_time AS NextResetTime, p.plan_type AS PlanType
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.provider_id = @ProviderId
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

            var results = await connection.QueryAsync<ProviderUsage>(sql, new { ProviderId = providerId });
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ProviderInfo>> GetProvidersAsync()
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderInfo>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                SELECT p.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       p.plan_type AS PlanType, p.is_active AS IsActive,
                       p.auth_source AS AuthSource, p.account_name AS AccountName,
                       (SELECT requests_percentage FROM provider_history 
                        WHERE provider_id = p.provider_id 
                        ORDER BY fetched_at DESC LIMIT 1) as LatestUsage,
                       (SELECT next_reset_time FROM provider_history 
                        WHERE provider_id = p.provider_id 
                        ORDER BY fetched_at DESC LIMIT 1) as NextResetTime
                FROM providers p
                WHERE p.is_active = 1
                ORDER BY p.provider_name";

            var results = await connection.QueryAsync<ProviderInfo>(sql);
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        if (!IsDatabaseAvailable())
            return new List<ResetEvent>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            var sql = $@"
                SELECT id AS Id, provider_id AS ProviderId, provider_name AS ProviderName,
                       previous_usage AS PreviousUsage, new_usage AS NewUsage,
                       reset_type AS ResetType, timestamp AS Timestamp
                FROM reset_events
                WHERE provider_id = @ProviderId
                ORDER BY timestamp DESC
                LIMIT {limit}";

            var results = await connection.QueryAsync<ResetEvent>(sql, new { ProviderId = providerId });
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<UsageSummary> GetUsageSummaryAsync()
    {
        if (!IsDatabaseAvailable())
            return new UsageSummary();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                SELECT 
                    COUNT(DISTINCT provider_id) as ProviderCount,
                    AVG(requests_percentage) as AverageUsage,
                    MAX(fetched_at) as LastUpdate
                FROM provider_history
                WHERE id IN (
                    SELECT MAX(id) FROM provider_history GROUP BY provider_id
                )";

            var result = await connection.QuerySingleOrDefaultAsync<UsageSummary>(sql);
            return result ?? new UsageSummary();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ChartDataPoint>> GetChartDataAsync(int hours = 24)
    {
        if (!IsDatabaseAvailable())
            return new List<ChartDataPoint>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();
            await EnsureChartIndexesAsync(connection);

            var cutoffUtc = DateTime.UtcNow.AddHours(-hours).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            const string sql = @"
                SELECT 
                    h.provider_id AS ProviderId,
                    p.provider_name AS ProviderName,
                    h.fetched_at AS Timestamp,
                    h.requests_percentage AS RequestsPercentage,
                    h.requests_used AS RequestsUsed
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.fetched_at >= @CutoffUtc
                ORDER BY h.fetched_at ASC";

            var results = await connection.QueryAsync<ChartDataPoint>(sql, new { CutoffUtc = cutoffUtc });
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ResetEvent>> GetRecentResetEventsAsync(int hours = 24)
    {
        if (!IsDatabaseAvailable())
            return new List<ResetEvent>();

        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();
            await EnsureChartIndexesAsync(connection);

            var cutoffUtc = DateTime.UtcNow.AddHours(-hours).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            const string sql = @"
                SELECT 
                    id AS Id, 
                    provider_id AS ProviderId, 
                    provider_name AS ProviderName,
                    previous_usage AS PreviousUsage, 
                    new_usage AS NewUsage,
                    reset_type AS ResetType, 
                    timestamp AS Timestamp
                FROM reset_events
                WHERE timestamp >= @CutoffUtc
                ORDER BY timestamp ASC";

            var results = await connection.QueryAsync<ResetEvent>(sql, new { CutoffUtc = cutoffUtc });
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<(List<Dictionary<string, object?>> rows, int totalCount)> GetProvidersRawAsync(int page = 1, int pageSize = 100)
    {
        return await GetTableRawAsync("providers", page, pageSize);
    }

    public async Task<(List<Dictionary<string, object?>> rows, int totalCount)> GetProviderHistoryRawAsync(int page = 1, int pageSize = 100)
    {
        return await GetTableRawAsync("provider_history", page, pageSize, "fetched_at DESC");
    }

    public async Task<(List<Dictionary<string, object?>> rows, int totalCount)> GetRawSnapshotsRawAsync(int page = 1, int pageSize = 100)
    {
        return await GetTableRawAsync("raw_snapshots", page, pageSize, "fetched_at DESC");
    }

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

            var totalCount = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName}");

            var sql = $"SELECT * FROM {tableName} {orderClause} LIMIT {pageSize} OFFSET {offset}";
            var rows = new List<Dictionary<string, object?>>();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
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

            return (rows, totalCount);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task EnsureChartIndexesAsync(SqliteConnection connection)
    {
        if (Interlocked.CompareExchange(ref _chartIndexesEnsured, 1, 0) != 0)
        {
            return;
        }

        const string sql = @"
            CREATE INDEX IF NOT EXISTS idx_history_fetched_at_asc ON provider_history(fetched_at ASC);
            CREATE INDEX IF NOT EXISTS idx_reset_timestamp_asc ON reset_events(timestamp ASC);
        ";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

public class ProviderInfo
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? AuthSource { get; set; }
    public string? AccountName { get; set; }
    public double LatestUsage { get; set; }
    public DateTime? NextResetTime { get; set; }
}

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

public class UsageSummary
{
    public int ProviderCount { get; set; }
    public double AverageUsage { get; set; }
    public string? LastUpdate { get; set; }
}

public class ChartDataPoint
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double RequestsPercentage { get; set; }
    public double RequestsUsed { get; set; }
}

