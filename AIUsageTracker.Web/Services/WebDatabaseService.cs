using Dapper;
using Microsoft.Data.Sqlite;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace AIUsageTracker.Web.Services;

public class WebDatabaseService : IWebDatabaseRepository
{
    private readonly string _dbPath;
    private readonly string _readConnectionString;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WebDatabaseService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private static int _chartIndexesEnsured;

    public WebDatabaseService(IMemoryCache cache, ILogger<WebDatabaseService> logger, IAppPathProvider pathProvider)
        : this(cache, logger, pathProvider, databasePathOverride: null)
    {
    }

    public WebDatabaseService(
        IMemoryCache cache,
        ILogger<WebDatabaseService> logger,
        IAppPathProvider pathProvider,
        string? databasePathOverride)
    {
        _cache = cache;
        _logger = logger;
        _dbPath = !string.IsNullOrWhiteSpace(databasePathOverride) 
            ? databasePathOverride 
            : pathProvider.GetDatabasePath();

        _readConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 10
        }.ToString();
    }

    public bool IsDatabaseAvailable()
    {
        return File.Exists(_dbPath);
    }

    public async Task<List<ProviderInfo>> GetProvidersAsync()
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderInfo>();

        var sw = Stopwatch.StartNew();
        using var connection = CreateReadConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT 
                p.provider_id AS ProviderId, 
                p.provider_name AS ProviderName, 
                p.is_active AS IsActive,
                p.auth_source AS AuthSource,
                p.account_name AS AccountName,
                h.requests_percentage AS LatestUsage,
                h.next_reset_time AS NextResetTime
            FROM providers p
            LEFT JOIN (
                SELECT provider_id, requests_percentage, next_reset_time, MAX(id)
                FROM provider_history
                GROUP BY provider_id
            ) h ON p.provider_id = h.provider_id
            WHERE p.is_active = 1";

        var results = (await connection.QueryAsync<ProviderInfo>(sql)).ToList();
        foreach (var p in results)
        {
            p.ProviderName = ProviderMetadataCatalog.GetDisplayName(p.ProviderId, p.ProviderName);
        }
        _logger.LogInformation("WebDB GetProvidersAsync count={Count} elapsedMs={ElapsedMs}",
            results.Count, sw.ElapsedMilliseconds);
        return results;
    }

    public async Task<List<ProviderUsage>> GetLatestUsageAsync(bool includeInactive = false)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        var sw = Stopwatch.StartNew();
        using var connection = CreateReadConnection();
        await connection.OpenAsync();

        string sql = @"
            SELECT h.*, p.provider_name as ProviderName 
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            WHERE h.id IN (SELECT MAX(id) FROM provider_history GROUP BY provider_id)";

        if (!includeInactive)
        {
            sql += " AND p.is_active = 1 AND h.is_available = 1";
        }

        var results = await connection.QueryAsync<dynamic>(sql);
        var list = results.Select(MapToProviderUsage).ToList();
        
        _logger.LogInformation("WebDB GetLatestUsageAsync count={Count} includeInactive={IncludeInactive} elapsedMs={ElapsedMs}",
            list.Count, includeInactive, sw.ElapsedMilliseconds);
        return list;
    }

    public async Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        using var connection = CreateReadConnection();
        await connection.OpenAsync();

        var sql = $@"
            SELECT h.*, p.provider_name as ProviderName
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            ORDER BY h.fetched_at DESC
            LIMIT {limit}";

        var results = await connection.QueryAsync<dynamic>(sql);
        return results.Select(MapToProviderUsage).ToList();
    }

    public async Task<List<ProviderUsage>> GetProviderHistoryAsync(string providerId, int limit = 100)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        using var connection = CreateReadConnection();
        await connection.OpenAsync();

        var sql = $@"
            SELECT h.*, p.provider_name as ProviderName
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            WHERE h.provider_id = @ProviderId
            ORDER BY h.fetched_at DESC
            LIMIT {limit}";

        var results = await connection.QueryAsync<dynamic>(sql, new { ProviderId = providerId });
        return results.Select(MapToProviderUsage).ToList();
    }

    public async Task<UsageSummary> GetUsageSummaryAsync()
    {
        var cacheKey = "db:usage-summary";
        if (_cache.TryGetValue<UsageSummary>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        if (!IsDatabaseAvailable())
            return new UsageSummary();

        var sw = Stopwatch.StartNew();
        using var connection = CreateReadConnection();
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

        var result = await connection.QuerySingleOrDefaultAsync<UsageSummary>(sql) ?? new UsageSummary();
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        _logger.LogInformation("WebDB GetUsageSummaryAsync providerCount={ProviderCount} elapsedMs={ElapsedMs}",
            result.ProviderCount, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<List<ProviderUsage>> GetHistorySamplesAsync(IEnumerable<string> providerIds, int lookbackHours, int maxSamples)
    {
        using var connection = CreateReadConnection();
        await connection.OpenAsync();

        var cutoffUtc = DateTime.UtcNow
            .AddHours(-lookbackHours)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            
        const string sql = @"
            WITH ranked AS (
                SELECT h.provider_id AS ProviderId,
                       h.requests_used AS RequestsUsed,
                       h.requests_available AS RequestsAvailable,
                       h.is_available AS IsAvailable,
                       h.response_latency_ms AS ResponseLatencyMs,
                       h.fetched_at AS FetchedAt,
                       ROW_NUMBER() OVER (PARTITION BY h.provider_id ORDER BY datetime(h.fetched_at) DESC) AS RowNum
                FROM provider_history h
                WHERE h.provider_id IN @ProviderIds
                  AND datetime(h.fetched_at) >= datetime(@CutoffUtc)
            )
            SELECT * FROM ranked
            WHERE RowNum <= @MaxSamples
            ORDER BY ProviderId, datetime(FetchedAt) ASC";

        var rows = await connection.QueryAsync<dynamic>(sql, new
        {
            ProviderIds = providerIds,
            CutoffUtc = cutoffUtc,
            MaxSamples = maxSamples
        });

        return rows.Select(MapToProviderUsage).ToList();
    }

    public async Task<List<ProviderUsage>> GetAllHistoryForExportAsync(int limit = 0)
    {
        using var connection = CreateReadConnection();
        await connection.OpenAsync();

        string sql = @"
            SELECT h.*, p.provider_name as ProviderName
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            ORDER BY h.fetched_at DESC";

        if (limit > 0) sql += $" LIMIT {limit}";

        var rows = await connection.QueryAsync<dynamic>(sql);
        return rows.Select(MapToProviderUsage).ToList();
    }

    private ProviderUsage MapToProviderUsage(dynamic row)
    {
        var usage = new ProviderUsage
        {
            ProviderId = row.provider_id ?? row.ProviderId,
            ProviderName = row.ProviderName,
            IsAvailable = row.is_available == 1 || (row.IsAvailable != null && row.IsAvailable == 1),
            Description = row.status_message ?? string.Empty,
            RequestsUsed = (double)(row.requests_used ?? row.RequestsUsed ?? 0.0),
            RequestsAvailable = (double)(row.requests_available ?? row.RequestsAvailable ?? 0.0),
            RequestsPercentage = (double)(row.requests_percentage ?? row.RequestsPercentage ?? 0.0),
            ResponseLatencyMs = (double)(row.response_latency_ms ?? row.ResponseLatencyMs ?? 0.0),
            FetchedAt = DateTime.Parse(row.fetched_at ?? row.FetchedAt)
        };

        if (row.next_reset_time != null)
        {
            usage.NextResetTime = DateTime.Parse(row.next_reset_time);
        }

        return usage;
    }

    public async Task<List<ChartDataPoint>> GetChartDataAsync(int hours = 24)
    {
        if (!IsDatabaseAvailable())
            return new List<ChartDataPoint>();

        var sw = Stopwatch.StartNew();

        using var connection = CreateReadConnection();
        await connection.OpenAsync();
        await EnsureChartIndexesAsync(connection);

        var cutoffUtc = DateTime.UtcNow.AddHours(-hours).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var bucketMinutes = hours switch
        {
            <= 24 => 1,
            <= 72 => 5,
            <= 168 => 15,
            _ => 60
        };
        var bucketSeconds = bucketMinutes * 60;

        const string sql = @"
            SELECT
                h.provider_id AS ProviderId,
                MIN(p.provider_name) AS ProviderName,
                datetime((strftime('%s', h.fetched_at) / @BucketSeconds) * @BucketSeconds, 'unixepoch') AS Timestamp,
                AVG(h.requests_percentage) AS RequestsPercentage,
                MAX(h.requests_used) AS RequestsUsed
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            WHERE h.fetched_at >= @CutoffUtc
            GROUP BY h.provider_id, (strftime('%s', h.fetched_at) / @BucketSeconds)
            ORDER BY Timestamp ASC";

        var results = await connection.QueryAsync<ChartDataPoint>(sql, new
        {
            CutoffUtc = cutoffUtc,
            BucketSeconds = bucketSeconds
        });
        var list = results.ToList();
        foreach (var point in list)
        {
            point.ProviderName = ProviderMetadataCatalog.GetDisplayName(point.ProviderId, point.ProviderName);
        }
        _logger.LogInformation("WebDB GetChartDataAsync hours={Hours} bucketMinutes={BucketMinutes} rows={Count} elapsedMs={ElapsedMs}",
            hours, bucketMinutes, list.Count, sw.ElapsedMilliseconds);
        return list;
    }

    public async Task<List<ResetEvent>> GetRecentResetEventsAsync(int hours = 24)
    {
        if (!IsDatabaseAvailable())
            return new List<ResetEvent>();

        var sw = Stopwatch.StartNew();
        using var connection = CreateReadConnection();
        await connection.OpenAsync();

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

        var results = (await connection.QueryAsync<ResetEvent>(sql, new { CutoffUtc = cutoffUtc })).ToList();
        foreach (var reset in results)
        {
            reset.ProviderName = ProviderMetadataCatalog.GetDisplayName(reset.ProviderId, reset.ProviderName);
        }
        return results;
    }

    public async Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        if (!IsDatabaseAvailable())
            return new List<ResetEvent>();

        using var connection = CreateReadConnection();
        await connection.OpenAsync();

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
            WHERE provider_id = @ProviderId
            ORDER BY timestamp DESC
            LIMIT @Limit";

        var results = (await connection.QueryAsync<ResetEvent>(sql, new { ProviderId = providerId, Limit = limit })).ToList();
        foreach (var reset in results)
        {
            reset.ProviderName = ProviderMetadataCatalog.GetDisplayName(reset.ProviderId, reset.ProviderName);
        }
        return results;
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
            using var connection = CreateReadConnection();
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

    private SqliteConnection CreateReadConnection()
    {
        return new SqliteConnection(_readConnectionString);
    }

    public string GetDatabasePath() => _dbPath;
}
