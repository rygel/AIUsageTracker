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

public class WebDatabaseService : IWebDatabaseRepository, IUsageAnalyticsService, IDataExportService
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
            
        // Deserialize details from JSON and set IsQuotaBased from provider class
        foreach (var usage in results)
        {
            if (!string.IsNullOrEmpty(usage.DetailsJson))
            {
                try
                {
                    usage.Details = JsonSerializer.Deserialize<List<ProviderUsageDetail>>(usage.DetailsJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Failed to deserialize Details JSON for provider {ProviderId}: {Json}", 
                        usage.ProviderId, usage.DetailsJson);
                }
            }
            
            if (ProviderMetadataCatalog.TryGet(usage.ProviderId, out var definition))
            {
                usage.IsQuotaBased = definition.IsQuotaBased;
                usage.PlanType = definition.PlanType;
            }
            usage.ProviderName = ProviderMetadataCatalog.GetDisplayName(usage.ProviderId, usage.ProviderName);
        }

        var list = results.ToList();
        _cache.Set(cacheKey, list, TimeSpan.FromMinutes(5));
        _logger.LogInformation("WebDB GetHistoryAsync rows={Count} elapsedMs={ElapsedMs}", list.Count, sw.ElapsedMilliseconds);
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
                       h.next_reset_time AS NextResetTime
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

        var results = await connection.QueryAsync<ProviderUsage>(sql);
        
        foreach (var usage in results)
        {
            if (ProviderMetadataCatalog.TryGet(usage.ProviderId, out var definition))
            {
                usage.IsQuotaBased = definition.IsQuotaBased;
                usage.PlanType = definition.PlanType;
            }
            usage.ProviderName = ProviderMetadataCatalog.GetDisplayName(usage.ProviderId, usage.ProviderName);
        }
        
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
                       h.next_reset_time AS NextResetTime
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.provider_id = @ProviderId
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

        var results = await connection.QueryAsync<ProviderUsage>(sql, new { ProviderId = providerId });
        
        foreach (var usage in results)
        {
            if (ProviderMetadataCatalog.TryGet(usage.ProviderId, out var definition))
            {
                usage.IsQuotaBased = definition.IsQuotaBased;
                usage.PlanType = definition.PlanType;
            }
            usage.ProviderName = ProviderMetadataCatalog.GetDisplayName(usage.ProviderId, usage.ProviderName);
        }
        
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
                       p.is_active AS IsActive,
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

        var results = (await connection.QueryAsync<ProviderInfo>(sql)).ToList();
        foreach (var provider in results)
        {
            provider.ProviderName = ProviderMetadataCatalog.GetDisplayName(provider.ProviderId, provider.ProviderName);
        }

        return results;
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

        var results = (await connection.QueryAsync<ResetEvent>(sql, new { ProviderId = providerId })).ToList();
        foreach (var reset in results)
        {
            reset.ProviderName = ProviderMetadataCatalog.GetDisplayName(reset.ProviderId, reset.ProviderName);
        }

        return results;
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
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        _logger.LogInformation("WebDB GetUsageSummaryAsync providerCount={ProviderCount} elapsedMs={ElapsedMs}",
            result.ProviderCount, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<IEnumerable<ProviderUsage>> GetHistorySamplesAsync(IEnumerable<string> providerIds, int lookbackHours, int maxSamplesPerProvider)
    {
        if (!IsDatabaseAvailable())
            return Enumerable.Empty<ProviderUsage>();

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
            SELECT ProviderId, RequestsUsed, RequestsAvailable, IsAvailable, ResponseLatencyMs, FetchedAt
            FROM ranked
            WHERE RowNum <= @MaxSamplesPerProvider
            ORDER BY ProviderId, datetime(FetchedAt) ASC";

        var rows = await connection.QueryAsync<ProviderUsage>(sql, new
        {
            ProviderIds = providerIds,
            CutoffUtc = cutoffUtc,
            MaxSamplesPerProvider = maxSamplesPerProvider
        });

        return rows;
    }

    public async Task<IEnumerable<dynamic>> GetAllHistoryForExportAsync(int? limit = null)
    {
        if (!IsDatabaseAvailable())
            return Enumerable.Empty<dynamic>();

        using var connection = CreateReadConnection();
        await connection.OpenAsync();

        var limitClause = limit.HasValue ? $" LIMIT {limit.Value}" : "";
        var sql = $@"
            SELECT h.provider_id, p.provider_name, h.requests_used, h.requests_available,
                   h.requests_percentage, h.is_available, h.status_message, h.fetched_at,
                   h.next_reset_time, h.details_json
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            ORDER BY h.fetched_at DESC{limitClause}";

        return await connection.QueryAsync<dynamic>(sql);
    }

    public async Task<Dictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(IEnumerable<string> providerIds, int lookbackHours = 72, int maxSamplesPerProvider = 720)
    {
        var samples = await GetHistorySamplesAsync(providerIds, lookbackHours, maxSamplesPerProvider);
        var forecasts = new Dictionary<string, BurnRateForecast>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var group in samples.GroupBy(s => s.ProviderId))
        {
            forecasts[group.Key] = UsageMath.CalculateBurnRateForecast(group.ToList());
        }
        
        return forecasts;
    }

    public async Task<Dictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(IEnumerable<string> providerIds, int lookbackHours = 168, int maxSamplesPerProvider = 1000)
    {
        var samples = await GetHistorySamplesAsync(providerIds, lookbackHours, maxSamplesPerProvider);
        var snapshots = new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var group in samples.GroupBy(s => s.ProviderId))
        {
            snapshots[group.Key] = UsageMath.CalculateReliabilitySnapshot(group);
        }
        
        return snapshots;
    }

    public async Task<Dictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(IEnumerable<string> providerIds, int lookbackHours = 72, int maxSamplesPerProvider = 720)
    {
        var samples = await GetHistorySamplesAsync(providerIds, lookbackHours, maxSamplesPerProvider);
        var snapshots = new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var group in samples.GroupBy(s => s.ProviderId))
        {
            snapshots[group.Key] = UsageMath.CalculateUsageAnomalySnapshot(group.ToList());
        }
        
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
        foreach (var reset in results)
        {
            reset.ProviderName = ProviderMetadataCatalog.GetDisplayName(reset.ProviderId, reset.ProviderName);
        }
        _cache.Set(cacheKey, results, TimeSpan.FromMinutes(5));
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

    // ========== Budget Policies (Experimental) ==========
    
    private static readonly List<BudgetPolicy> _defaultBudgetPolicies = new()
    {
        new BudgetPolicy { Id = "default-monthly-100", ProviderId = "all", Name = "Monthly Budget", Period = BudgetPeriod.Monthly, Limit = 100, Currency = "USD" },
        new BudgetPolicy { Id = "default-weekly-25", ProviderId = "all", Name = "Weekly Budget", Period = BudgetPeriod.Weekly, Limit = 25, Currency = "USD" }
    };

    public async Task<List<BudgetStatus>> GetBudgetStatusesAsync(List<string> providerIds)
    {
        var statuses = new List<BudgetStatus>();
        
        foreach (var policy in _defaultBudgetPolicies)
        {
            var period = GetPeriodRange(policy.Period);
            var start = period.start;
            var end = period.end;

            var usage = await GetUsageInRangeAsync(providerIds, start, end);
            
            var status = new BudgetStatus
            {
                ProviderId = policy.ProviderId,
                ProviderName = policy.Name,
                BudgetLimit = policy.Limit,
                CurrentSpend = usage,
                RemainingBudget = Math.Max(0, policy.Limit - usage),
                UtilizationPercent = policy.Limit > 0 ? (usage / policy.Limit) * 100 : 0,
                Period = policy.Period
            };
            statuses.Add(status);
        }

        // Also calculate per-provider budgets based on current usage
        foreach (var providerId in providerIds)
        {
            var usage = await GetUsageInRangeAsync(new List<string> { providerId }, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
            var monthlyRate = usage / 30.0;
            
            var status = new BudgetStatus
            {
                ProviderId = providerId,
                ProviderName = ProviderMetadataCatalog.GetDisplayName(providerId),
                BudgetLimit = 50, // Default implied budget
                CurrentSpend = usage,
                RemainingBudget = Math.Max(0, 50 - usage),
                UtilizationPercent = (usage / 50.0) * 100,
                Period = BudgetPeriod.Monthly
            };
            statuses.Add(status);
        }

        return statuses;
    }

    private static (DateTime start, DateTime end) GetPeriodRange(BudgetPeriod period)
    {
        var now = DateTime.UtcNow;
        return period switch
        {
            BudgetPeriod.Daily => (now.Date, now),
            BudgetPeriod.Weekly => (now.Date.AddDays(-(int)now.DayOfWeek), now),
            BudgetPeriod.Monthly => (new DateTime(now.Year, now.Month, 1), now),
            BudgetPeriod.Yearly => (new DateTime(now.Year, 1, 1), now),
            _ => (now.Date.AddDays(-30), now)
        };
    }

    private async Task<double> GetUsageInRangeAsync(List<string> providerIds, DateTime start, DateTime end)
    {
        if (!IsDatabaseAvailable() || providerIds.Count == 0)
            return 0;

        try
        {
            await using var connection = new SqliteConnection(_readConnectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT COALESCE(SUM(requests_used), 0) as TotalUsage
                FROM provider_history
                WHERE provider_id IN @ProviderIds 
                AND fetched_at >= @Start 
                AND fetched_at <= @End";

            var result = await connection.QuerySingleOrDefaultAsync<double>(sql, new 
            { 
                ProviderIds = providerIds, 
                Start = start.ToString("yyyy-MM-dd HH:mm:ss"), 
                End = end.ToString("yyyy-MM-dd HH:mm:ss") 
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating usage in range");
            return 0;
        }
    }

    // ========== Usage Comparison (Experimental) ==========

    public async Task<List<UsageComparison>> GetUsageComparisonsAsync(List<string> providerIds)
    {
        var comparisons = new List<UsageComparison>();
        
        if (!IsDatabaseAvailable() || providerIds.Count == 0)
            return comparisons;

        var now = DateTime.UtcNow;
        
        // Define comparison periods
        var periods = new[]
        {
            ("This Week", now.AddDays(-(int)now.DayOfWeek), now, now.AddDays(-7).AddDays(-(int)now.DayOfWeek), now.AddDays(-7)),
            ("Last Week", now.AddDays(-7).AddDays(-(int)now.DayOfWeek), now.AddDays(-7), now.AddDays(-14).AddDays(-(int)now.DayOfWeek), now.AddDays(-14)),
            ("This Month", new DateTime(now.Year, now.Month, 1), now, new DateTime(now.Year, now.Month, 1).AddMonths(-1), new DateTime(now.Year, now.Month, 1)),
            ("Last Month", new DateTime(now.Year, now.Month, 1).AddMonths(-1), new DateTime(now.Year, now.Month, 1), new DateTime(now.Year, now.Month, 1).AddMonths(-2), new DateTime(now.Year, now.Month, 1).AddMonths(-1))
        };

        foreach (var providerId in providerIds)
        {
            foreach (var (label, currStart, currEnd, prevStart, prevEnd) in periods)
            {
                var currentUsage = await GetUsageInRangeAsync(new List<string> { providerId }, currStart, currEnd);
                var previousUsage = await GetUsageInRangeAsync(new List<string> { providerId }, prevStart, prevEnd);
                
                var changeAbs = currentUsage - previousUsage;
                var changePct = previousUsage > 0 ? ((currentUsage - previousUsage) / previousUsage) * 100 : (currentUsage > 0 ? 100 : 0);

                comparisons.Add(new UsageComparison
                {
                    ProviderId = providerId,
                    ProviderName = ProviderMetadataCatalog.GetDisplayName(providerId),
                    PeriodStart = currStart,
                    PeriodEnd = currEnd,
                    PreviousPeriodStart = prevStart,
                    PreviousPeriodEnd = prevEnd,
                    CurrentPeriodUsage = currentUsage,
                    PreviousPeriodUsage = previousUsage,
                    ChangeAbsolute = changeAbs,
                    ChangePercent = changePct
                });
            }
        }

        return comparisons;
    }

    // ========== Data Export ==========

    public async Task<string> ExportHistoryToCsvAsync()
    {
        var rows = await GetAllHistoryForExportAsync();
        
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("provider_id,provider_name,requests_used,requests_available,requests_percentage,is_available,status_message,fetched_at,next_reset_time");

        foreach (var row in rows)
        {
            var isAvail = row.is_available;
            int availInt = 0;
            if (isAvail is bool b) availInt = b ? 1 : 0;
            else if (isAvail is long l) availInt = (int)l;
            else if (isAvail is int i) availInt = i;

            sb.AppendLine($"\"{row.provider_id}\",\"{row.provider_name}\",{row.requests_used},{row.requests_available},{row.requests_percentage},{availInt},\"{row.status_message?.Replace("\"", "\"\"")}\",\"{row.fetched_at}\",\"{row.next_reset_time}\"");
        }

        return sb.ToString();
    }

    public async Task<string> ExportHistoryToJsonAsync()
    {
        var rows = await GetAllHistoryForExportAsync();
        
        var results = rows.Select(r => new 
        {
            provider_id = (string)r.provider_id,
            provider_name = (string)r.provider_name,
            requests_used = (double)r.requests_used,
            requests_available = (double)r.requests_available,
            requests_percentage = (double)r.requests_percentage,
            is_available = (bool)r.is_available,
            status_message = (string)r.status_message,
            fetched_at = ((DateTime)r.fetched_at).ToString("O"),
            next_reset_time = r.next_reset_time != null ? ((DateTime)r.next_reset_time).ToString("O") : null
        }).Take(10000).ToList();

        return System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }

    public async Task<byte[]?> CreateDatabaseBackupAsync()
    {
        if (!IsDatabaseAvailable())
            return null;

        try
        {
            var dbBytes = await File.ReadAllBytesAsync(_dbPath);
            return dbBytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error creating database backup");
            return null;
        }
    }

    public string GetDatabasePath() => _dbPath;

    private sealed class ProviderReliabilityRow
    {
        public string ProviderId { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public double ResponseLatencyMs { get; set; }
        public DateTime FetchedAt { get; set; }
    }
}
