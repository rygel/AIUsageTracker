using Dapper;
using Microsoft.Data.Sqlite;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace AIUsageTracker.Web.Services;

public class WebDatabaseService
{
    private readonly string _dbPath;
    private readonly string _readConnectionString;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WebDatabaseService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private static int _chartIndexesEnsured;

    public WebDatabaseService(IMemoryCache cache, ILogger<WebDatabaseService> logger)
        : this(cache, logger, databasePathOverride: null)
    {
    }

    public WebDatabaseService(
        IMemoryCache cache,
        ILogger<WebDatabaseService> logger,
        string? databasePathOverride)
    {
        _cache = cache;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(databasePathOverride))
        {
            _dbPath = databasePathOverride;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbDir = ResolveDatabaseDirectory(appData);
            _dbPath = Path.Combine(dbDir, "usage.db");
        }

        _readConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 10
        }.ToString();
    }

    private static string ResolveDatabaseDirectory(string appData)
    {
        var primaryDir = Path.Combine(appData, "AIUsageTracker");
        var legacyDir = Path.Combine(appData, "AIConsumptionTracker");

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

    public async Task<List<ProviderUsage>> GetLatestUsageAsync(bool includeInactive = false)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        var cacheKey = $"db:latest-usage:{includeInactive}";
        if (_cache.TryGetValue<List<ProviderUsage>>(cacheKey, out var cached) && cached != null)
        {
            _logger.LogDebug("WebDB cache hit for GetLatestUsageAsync(includeInactive={IncludeInactive}) count={Count}",
                includeInactive, cached.Count);
            return cached;
        }

        var sw = Stopwatch.StartNew();

        using var connection = CreateReadConnection();
        await connection.OpenAsync();

            const string activeSql = @"
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
                AND h.is_available = 1
                ORDER BY p.provider_name";

            const string allSql = @"
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

        var sql = includeInactive ? allSql : activeSql;
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

        var list = results.ToList();
        _cache.Set(cacheKey, list, TimeSpan.FromSeconds(8));
        _logger.LogInformation("WebDB GetLatestUsageAsync(includeInactive={IncludeInactive}) rows={Count} elapsedMs={ElapsedMs}",
            includeInactive, list.Count, sw.ElapsedMilliseconds);
        return list;
    }

    public async Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        using var connection = CreateReadConnection();
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

    public async Task<List<ProviderUsage>> GetProviderHistoryAsync(string providerId, int limit = 100)
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderUsage>();

        using var connection = CreateReadConnection();
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

    public async Task<List<ProviderInfo>> GetProvidersAsync()
    {
        if (!IsDatabaseAvailable())
            return new List<ProviderInfo>();

        using var connection = CreateReadConnection();
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

    public async Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        if (!IsDatabaseAvailable())
            return new List<ResetEvent>();

        using var connection = CreateReadConnection();
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

    public async Task<UsageSummary> GetUsageSummaryAsync()
    {
        if (!IsDatabaseAvailable())
            return new UsageSummary();

        const string cacheKey = "db:usage-summary";
        if (_cache.TryGetValue<UsageSummary>(cacheKey, out var cached) && cached != null)
        {
            _logger.LogDebug("WebDB cache hit for GetUsageSummaryAsync");
            return cached;
        }

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
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(12));
        _logger.LogInformation("WebDB GetUsageSummaryAsync providerCount={ProviderCount} elapsedMs={ElapsedMs}",
            result.ProviderCount, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<Dictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 72,
        int maxSamplesPerProvider = 720)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lookbackHours);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamplesPerProvider);

        if (!IsDatabaseAvailable())
        {
            return new Dictionary<string, BurnRateForecast>(StringComparer.OrdinalIgnoreCase);
        }

        ArgumentNullException.ThrowIfNull(providerIds);

        var normalizedProviderIds = providerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedProviderIds.Count == 0)
        {
            return new Dictionary<string, BurnRateForecast>(StringComparer.OrdinalIgnoreCase);
        }

        var cacheKey = $"db:burn-rate:{lookbackHours}:{maxSamplesPerProvider}:{string.Join(",", normalizedProviderIds)}";
        if (_cache.TryGetValue<Dictionary<string, BurnRateForecast>>(cacheKey, out var cached) && cached != null)
        {
            _logger.LogDebug("WebDB cache hit for GetBurnRateForecastsAsync providers={Count}", normalizedProviderIds.Count);
            return cached;
        }

        var sw = Stopwatch.StartNew();
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
                       h.fetched_at AS FetchedAt,
                       ROW_NUMBER() OVER (PARTITION BY h.provider_id ORDER BY datetime(h.fetched_at) DESC) AS RowNum
                FROM provider_history h
                WHERE h.provider_id IN @ProviderIds
                  AND datetime(h.fetched_at) >= datetime(@CutoffUtc)
            )
            SELECT ProviderId, RequestsUsed, RequestsAvailable, IsAvailable, FetchedAt
            FROM ranked
            WHERE RowNum <= @MaxSamplesPerProvider
            ORDER BY ProviderId, datetime(FetchedAt) ASC";

        var rows = (await connection.QueryAsync<BurnRateSampleRow>(sql, new
        {
            ProviderIds = normalizedProviderIds,
            CutoffUtc = cutoffUtc,
            MaxSamplesPerProvider = maxSamplesPerProvider
        })).ToList();

        var forecasts = normalizedProviderIds.ToDictionary(
            id => id,
            _ => BurnRateForecast.Unavailable("Insufficient history"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in rows.GroupBy(r => r.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var samples = group
                .Where(x => x.IsAvailable)
                .Select(x => new ProviderUsage
                {
                    ProviderId = x.ProviderId,
                    RequestsUsed = x.RequestsUsed,
                    RequestsAvailable = x.RequestsAvailable,
                    IsAvailable = x.IsAvailable,
                    FetchedAt = x.FetchedAt
                })
                .ToList();

            forecasts[group.Key] = UsageMath.CalculateBurnRateForecast(samples);
        }

        _cache.Set(cacheKey, forecasts, TimeSpan.FromSeconds(30));
        _logger.LogInformation(
            "WebDB GetBurnRateForecastsAsync providers={ProviderCount} rows={RowCount} elapsedMs={ElapsedMs}",
            normalizedProviderIds.Count,
            rows.Count,
            sw.ElapsedMilliseconds);

        return forecasts;
    }

    public async Task<Dictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 168,
        int maxSamplesPerProvider = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lookbackHours);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamplesPerProvider);

        if (!IsDatabaseAvailable())
        {
            return new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        ArgumentNullException.ThrowIfNull(providerIds);

        var normalizedProviderIds = providerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedProviderIds.Count == 0)
        {
            return new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var cacheKey = $"db:reliability:{lookbackHours}:{maxSamplesPerProvider}:{string.Join(",", normalizedProviderIds)}";
        if (_cache.TryGetValue<Dictionary<string, ProviderReliabilitySnapshot>>(cacheKey, out var cached) && cached != null)
        {
            _logger.LogDebug("WebDB cache hit for GetProviderReliabilityAsync providers={Count}", normalizedProviderIds.Count);
            return cached;
        }

        var sw = Stopwatch.StartNew();
        using var connection = CreateReadConnection();
        await connection.OpenAsync();

        var cutoffUtc = DateTime.UtcNow
            .AddHours(-lookbackHours)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        const string sqlWithLatency = @"
            WITH ranked AS (
                SELECT h.provider_id AS ProviderId,
                       h.is_available AS IsAvailable,
                       h.response_latency_ms AS ResponseLatencyMs,
                       h.fetched_at AS FetchedAt,
                       ROW_NUMBER() OVER (PARTITION BY h.provider_id ORDER BY datetime(h.fetched_at) DESC) AS RowNum
                FROM provider_history h
                WHERE h.provider_id IN @ProviderIds
                  AND datetime(h.fetched_at) >= datetime(@CutoffUtc)
            )
            SELECT ProviderId, IsAvailable, ResponseLatencyMs, FetchedAt
            FROM ranked
            WHERE RowNum <= @MaxSamplesPerProvider
            ORDER BY ProviderId, datetime(FetchedAt) ASC";
        const string sqlWithoutLatency = @"
            WITH ranked AS (
                SELECT h.provider_id AS ProviderId,
                       h.is_available AS IsAvailable,
                       0 AS ResponseLatencyMs,
                       h.fetched_at AS FetchedAt,
                       ROW_NUMBER() OVER (PARTITION BY h.provider_id ORDER BY datetime(h.fetched_at) DESC) AS RowNum
                FROM provider_history h
                WHERE h.provider_id IN @ProviderIds
                  AND datetime(h.fetched_at) >= datetime(@CutoffUtc)
            )
            SELECT ProviderId, IsAvailable, ResponseLatencyMs, FetchedAt
            FROM ranked
            WHERE RowNum <= @MaxSamplesPerProvider
            ORDER BY ProviderId, datetime(FetchedAt) ASC";

        List<ProviderReliabilityRow> rows;
        try
        {
            rows = (await connection.QueryAsync<ProviderReliabilityRow>(sqlWithLatency, new
            {
                ProviderIds = normalizedProviderIds,
                CutoffUtc = cutoffUtc,
                MaxSamplesPerProvider = maxSamplesPerProvider
            })).ToList();
        }
        catch (SqliteException ex) when (ex.Message.Contains("response_latency_ms", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "provider_history.response_latency_ms is unavailable; falling back to zero latency snapshots until migration runs.");
            rows = (await connection.QueryAsync<ProviderReliabilityRow>(sqlWithoutLatency, new
            {
                ProviderIds = normalizedProviderIds,
                CutoffUtc = cutoffUtc,
                MaxSamplesPerProvider = maxSamplesPerProvider
            })).ToList();
        }

        var snapshots = normalizedProviderIds.ToDictionary(
            id => id,
            _ => ProviderReliabilitySnapshot.Unavailable("No history"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in rows.GroupBy(r => r.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var samples = group.Select(x => new ProviderUsage
            {
                ProviderId = x.ProviderId,
                IsAvailable = x.IsAvailable,
                ResponseLatencyMs = x.ResponseLatencyMs,
                FetchedAt = x.FetchedAt
            });

            snapshots[group.Key] = UsageMath.CalculateReliabilitySnapshot(samples);
        }

        _cache.Set(cacheKey, snapshots, TimeSpan.FromSeconds(30));
        _logger.LogInformation(
            "WebDB GetProviderReliabilityAsync providers={ProviderCount} rows={RowCount} elapsedMs={ElapsedMs}",
            normalizedProviderIds.Count,
            rows.Count,
            sw.ElapsedMilliseconds);

        return snapshots;
    }

    public async Task<Dictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 72,
        int maxSamplesPerProvider = 720)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lookbackHours);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamplesPerProvider);

        if (!IsDatabaseAvailable())
        {
            return new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        ArgumentNullException.ThrowIfNull(providerIds);

        var normalizedProviderIds = providerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedProviderIds.Count == 0)
        {
            return new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var cacheKey = $"db:anomaly:{lookbackHours}:{maxSamplesPerProvider}:{string.Join(",", normalizedProviderIds)}";
        if (_cache.TryGetValue<Dictionary<string, UsageAnomalySnapshot>>(cacheKey, out var cached) && cached != null)
        {
            _logger.LogDebug("WebDB cache hit for GetUsageAnomaliesAsync providers={Count}", normalizedProviderIds.Count);
            return cached;
        }

        var sw = Stopwatch.StartNew();
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
                       h.fetched_at AS FetchedAt,
                       ROW_NUMBER() OVER (PARTITION BY h.provider_id ORDER BY datetime(h.fetched_at) DESC) AS RowNum
                FROM provider_history h
                WHERE h.provider_id IN @ProviderIds
                  AND datetime(h.fetched_at) >= datetime(@CutoffUtc)
            )
            SELECT ProviderId, RequestsUsed, RequestsAvailable, IsAvailable, FetchedAt
            FROM ranked
            WHERE RowNum <= @MaxSamplesPerProvider
            ORDER BY ProviderId, datetime(FetchedAt) ASC";

        var rows = (await connection.QueryAsync<BurnRateSampleRow>(sql, new
        {
            ProviderIds = normalizedProviderIds,
            CutoffUtc = cutoffUtc,
            MaxSamplesPerProvider = maxSamplesPerProvider
        })).ToList();

        var snapshots = normalizedProviderIds.ToDictionary(
            id => id,
            _ => UsageAnomalySnapshot.Unavailable("Insufficient history"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in rows.GroupBy(r => r.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var samples = group
                .Where(x => x.IsAvailable)
                .Select(x => new ProviderUsage
                {
                    ProviderId = x.ProviderId,
                    RequestsUsed = x.RequestsUsed,
                    RequestsAvailable = x.RequestsAvailable,
                    IsAvailable = x.IsAvailable,
                    FetchedAt = x.FetchedAt
                })
                .ToList();

            snapshots[group.Key] = UsageMath.CalculateUsageAnomalySnapshot(samples);
        }

        _cache.Set(cacheKey, snapshots, TimeSpan.FromSeconds(30));
        _logger.LogInformation(
            "WebDB GetUsageAnomaliesAsync providers={ProviderCount} rows={RowCount} elapsedMs={ElapsedMs}",
            normalizedProviderIds.Count,
            rows.Count,
            sw.ElapsedMilliseconds);

        return snapshots;
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
        _logger.LogInformation("WebDB GetChartDataAsync hours={Hours} bucketMinutes={BucketMinutes} rows={Count} elapsedMs={ElapsedMs}",
            hours, bucketMinutes, list.Count, sw.ElapsedMilliseconds);
        return list;
    }

    public async Task<List<ResetEvent>> GetRecentResetEventsAsync(int hours = 24)
    {
        if (!IsDatabaseAvailable())
            return new List<ResetEvent>();

        var cacheKey = $"db:recent-reset-events:{hours}";
        if (_cache.TryGetValue<List<ResetEvent>>(cacheKey, out var cached) && cached != null)
        {
            _logger.LogDebug("WebDB cache hit for GetRecentResetEventsAsync(hours={Hours}) count={Count}",
                hours, cached.Count);
            return cached;
        }

        var sw = Stopwatch.StartNew();

        using var connection = CreateReadConnection();
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

        var results = (await connection.QueryAsync<ResetEvent>(sql, new { CutoffUtc = cutoffUtc })).ToList();
        _cache.Set(cacheKey, results, TimeSpan.FromSeconds(15));
        _logger.LogInformation("WebDB GetRecentResetEventsAsync hours={Hours} rows={Count} elapsedMs={ElapsedMs}",
            hours, results.Count, sw.ElapsedMilliseconds);
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

    private sealed class BurnRateSampleRow
    {
        public string ProviderId { get; set; } = string.Empty;
        public double RequestsUsed { get; set; }
        public double RequestsAvailable { get; set; }
        public bool IsAvailable { get; set; }
        public DateTime FetchedAt { get; set; }
    }

    private sealed class ProviderReliabilityRow
    {
        public string ProviderId { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public double ResponseLatencyMs { get; set; }
        public DateTime FetchedAt { get; set; }
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

