// <copyright file="WebDatabaseService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Globalization;

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

using Dapper;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;

namespace AIUsageTracker.Web.Services;

public class WebDatabaseService : IWebDatabaseRepository
{
    private const string ProvidersSql = @"
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

    private const string UsageSummarySql = @"
            SELECT 
                COUNT(DISTINCT provider_id) as ProviderCount,
                AVG(requests_percentage) as AverageUsage,
                MAX(fetched_at) as LastUpdate
            FROM provider_history
            WHERE id IN (
                SELECT MAX(id) FROM provider_history GROUP BY provider_id
            )";

    private const string HistorySamplesSql = @"
            WITH normalized AS (
                SELECT h.provider_id AS ProviderId,
                       h.requests_used AS RequestsUsed,
                       h.requests_available AS RequestsAvailable,
                       h.is_available AS IsAvailable,
                       h.response_latency_ms AS ResponseLatencyMs,
                       CASE
                           WHEN typeof(h.fetched_at) IN ('integer', 'real')
                               THEN datetime(CAST(h.fetched_at AS INTEGER), 'unixepoch')
                           ELSE datetime(h.fetched_at)
                       END AS FetchedAtUtc
                FROM provider_history h
                WHERE h.provider_id IN @ProviderIds
            ),
            ranked AS (
                SELECT ProviderId,
                       RequestsUsed,
                       RequestsAvailable,
                       IsAvailable,
                       ResponseLatencyMs,
                       FetchedAtUtc AS FetchedAt,
                       ROW_NUMBER() OVER (PARTITION BY ProviderId ORDER BY datetime(FetchedAtUtc) DESC) AS RowNum
                FROM normalized
                WHERE datetime(FetchedAtUtc) >= datetime(@CutoffUtc)
            )
            SELECT * FROM ranked
            WHERE RowNum <= @MaxSamples
            ORDER BY ProviderId, datetime(FetchedAt) ASC";

    private const string ChartDataSql = @"
            SELECT
                h.provider_id AS ProviderId,
                MIN(p.provider_name) AS ProviderName,
                datetime((strftime('%s', h.fetched_at) / @BucketSeconds) * @BucketSeconds, 'unixepoch') AS Timestamp,
                AVG(h.requests_percentage) AS UsedPercent,
                MAX(h.requests_used) AS RequestsUsed
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            WHERE h.fetched_at >= @CutoffUtc
            GROUP BY h.provider_id, (strftime('%s', h.fetched_at) / @BucketSeconds)
            ORDER BY Timestamp ASC";

    private const string RecentResetEventsSql = @"
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

    private const string ProviderResetEventsSql = @"
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

    private readonly WebDatabaseConnectionFactory _connectionFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WebDatabaseService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public WebDatabaseService(IMemoryCache cache, ILogger<WebDatabaseService> logger, IAppPathProvider pathProvider)
        : this(cache, logger, pathProvider, connectionFactory: null, databasePathOverride: null)
    {
    }

    internal WebDatabaseService(
        IMemoryCache cache,
        ILogger<WebDatabaseService> logger,
        IAppPathProvider pathProvider,
        WebDatabaseConnectionFactory? connectionFactory,
        string? databasePathOverride)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        this._cache = cache;
        this._logger = logger;
        this._connectionFactory = connectionFactory
            ?? new WebDatabaseConnectionFactory(
                !string.IsNullOrWhiteSpace(databasePathOverride)
                    ? databasePathOverride
                    : pathProvider.GetDatabasePath());
    }

    public bool IsDatabaseAvailable()
    {
        return this._connectionFactory.IsDatabaseAvailable();
    }

    public async Task<IReadOnlyList<ProviderInfo>> GetProvidersAsync()
    {
        var sw = Stopwatch.StartNew();

        var results = await this.QueryIfDatabaseAvailableAsync(
            async connection => (await connection.QueryAsync<ProviderInfo>(ProvidersSql).ConfigureAwait(false)).ToList(),
            []).ConfigureAwait(false);
        WebProviderDisplayNameMapper.Apply(results);

        this._logger.LogInformation(
            "WebDB GetProvidersAsync count={Count} elapsedMs={ElapsedMs}",
            results.Count,
            sw.ElapsedMilliseconds);
        return results;
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetLatestUsageAsync(bool includeInactive = false)
    {
        var sw = Stopwatch.StartNew();

        var list = await this.QueryUsageListIfDatabaseAvailableAsync(
            connection => connection.QueryAsync<dynamic>(WebDatabaseQueryBuilder.BuildLatestUsageQuery(includeInactive))).ConfigureAwait(false);

        this._logger.LogInformation(
            "WebDB GetLatestUsageAsync count={Count} includeInactive={IncludeInactive} elapsedMs={ElapsedMs}",
            list.Count,
            includeInactive,
            sw.ElapsedMilliseconds);
        return list;
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        return await this.QueryUsageListIfDatabaseAvailableAsync(
            connection => connection.QueryAsync<dynamic>(WebDatabaseQueryBuilder.BuildHistoryQuery(limit))).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetProviderHistoryAsync(string providerId, int limit = 100)
    {
        return await this.QueryUsageListIfDatabaseAvailableAsync(
            connection => connection.QueryAsync<dynamic>(WebDatabaseQueryBuilder.BuildProviderHistoryQuery(limit), new { ProviderId = providerId })).ConfigureAwait(false);
    }

    public async Task<UsageSummary> GetUsageSummaryAsync()
    {
        var cacheKey = "db:usage-summary";
        if (this._cache.TryGetValue<UsageSummary>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        if (!this.IsDatabaseAvailable())
        {
            return new UsageSummary();
        }

        var sw = Stopwatch.StartNew();

        var result = await this.QuerySingleIfDatabaseAvailableAsync(
            connection => connection.QuerySingleOrDefaultAsync<UsageSummary>(UsageSummarySql),
            new UsageSummary()).ConfigureAwait(false);
        this._cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        this._logger.LogInformation(
            "WebDB GetUsageSummaryAsync providerCount={ProviderCount} elapsedMs={ElapsedMs}",
            result.ProviderCount,
            sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetHistorySamplesAsync(IEnumerable<string> providerIds, int lookbackHours, int maxSamples)
    {
        var cutoffUtc = DateTime.UtcNow
            .AddHours(-lookbackHours)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        return await this.QueryUsageListIfDatabaseAvailableAsync(
            async connection => await connection.QueryAsync<dynamic>(HistorySamplesSql, new
            {
                ProviderIds = providerIds,
                CutoffUtc = cutoffUtc,
                MaxSamples = maxSamples,
            }).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetAllHistoryForExportAsync(int limit = 0)
    {
        return await this.QueryUsageListIfDatabaseAvailableAsync(
            connection => connection.QueryAsync<dynamic>(WebDatabaseQueryBuilder.BuildExportHistoryQuery(limit))).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChartDataPoint>> GetChartDataAsync(int hours = 24)
    {
        var sw = Stopwatch.StartNew();

        var cutoffUtc = DateTime.UtcNow.AddHours(-hours).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var bucketMinutes = hours switch
        {
            <= 24 => 1,
            <= 72 => 5,
            <= 168 => 15,
            _ => 60,
        };
        var bucketSeconds = bucketMinutes * 60;

        var list = await this.QueryDisplayNamedListIfDatabaseAvailableAsync(
            connection => connection.QueryAsync<ChartDataPoint>(ChartDataSql, new
            {
                CutoffUtc = cutoffUtc,
                BucketSeconds = bucketSeconds,
            }),
            []).ConfigureAwait(false);

        this._logger.LogInformation(
            "WebDB GetChartDataAsync hours={Hours} bucketMinutes={BucketMinutes} rows={Count} elapsedMs={ElapsedMs}",
            hours,
            bucketMinutes,
            list.Count,
            sw.ElapsedMilliseconds);
        return list;
    }

    public async Task<IReadOnlyList<ResetEvent>> GetRecentResetEventsAsync(int hours = 24)
    {
        var sw = Stopwatch.StartNew();

        var cutoffUtc = DateTime.UtcNow.AddHours(-hours).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        var results = await this.QueryDisplayNamedListIfDatabaseAvailableAsync(
            connection => connection.QueryAsync<ResetEvent>(RecentResetEventsSql, new { CutoffUtc = cutoffUtc }),
            []).ConfigureAwait(false);
        this._logger.LogInformation(
            "WebDB GetRecentResetEventsAsync hours={Hours} count={Count} elapsedMs={ElapsedMs}",
            hours,
            results.Count,
            sw.ElapsedMilliseconds);
        return results;
    }

    public async Task<IReadOnlyList<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        var results = await this.QueryDisplayNamedListIfDatabaseAvailableAsync(
            connection => connection.QueryAsync<ResetEvent>(ProviderResetEventsSql, new { ProviderId = providerId, Limit = limit }),
            []).ConfigureAwait(false);
        return results;
    }

    public async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int TotalCount)> GetProvidersRawAsync(int page = 1, int pageSize = 100)
    {
        return await this.GetTableRawAsync("providers", page, pageSize).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int TotalCount)> GetProviderHistoryRawAsync(int page = 1, int pageSize = 100)
    {
        return await this.GetTableRawAsync("provider_history", page, pageSize, "fetched_at DESC").ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int TotalCount)> GetRawSnapshotsRawAsync(int page = 1, int pageSize = 100)
    {
        return await this.GetTableRawAsync("raw_snapshots", page, pageSize, "fetched_at DESC").ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int TotalCount)> GetResetEventsRawAsync(int page = 1, int pageSize = 100)
    {
        return await this.GetTableRawAsync("reset_events", page, pageSize, "timestamp DESC").ConfigureAwait(false);
    }

    public string GetDatabasePath() => this._connectionFactory.GetDatabasePath();

    private async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int TotalCount)> GetTableRawAsync(string tableName, int page, int pageSize, string? orderBy = null)
    {
        if (!this.IsDatabaseAvailable())
        {
            return ([], 0);
        }

        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = this.CreateReadConnection();
            await connection.OpenAsync().ConfigureAwait(false);

            return await WebDatabaseRawTableReader.ReadTableAsync(connection, tableName, page, pageSize, orderBy)
                .ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    private async Task<T> QueryIfDatabaseAvailableAsync<T>(
        Func<SqliteConnection, Task<T>> queryAsync,
        T unavailableValue)
    {
        if (!this.IsDatabaseAvailable())
        {
            return unavailableValue;
        }

        using var connection = this.CreateReadConnection();
        await connection.OpenAsync().ConfigureAwait(false);
        return await queryAsync(connection).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ProviderUsage>> QueryUsageListIfDatabaseAvailableAsync(
        Func<SqliteConnection, Task<IEnumerable<dynamic>>> queryAsync)
    {
        return await this.QueryIfDatabaseAvailableAsync(
            async connection =>
            {
                var rows = await queryAsync(connection).ConfigureAwait(false);
                return rows.Select(WebProviderUsageMapper.Map).ToList();
            },
            []).ConfigureAwait(false);
    }

    private async Task<T> QuerySingleIfDatabaseAvailableAsync<T>(
        Func<SqliteConnection, Task<T?>> queryAsync,
        T unavailableValue)
        where T : class
    {
        return await this.QueryIfDatabaseAvailableAsync(
            async connection => await queryAsync(connection).ConfigureAwait(false) ?? unavailableValue,
            unavailableValue).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> QueryDisplayNamedListIfDatabaseAvailableAsync<T>(
        Func<SqliteConnection, Task<IEnumerable<T>>> queryAsync,
        IReadOnlyList<T> unavailableValue)
    {
        return await this.QueryIfDatabaseAvailableAsync<IReadOnlyList<T>>(
            async connection =>
            {
                var results = (await queryAsync(connection).ConfigureAwait(false)).ToList();
                WebProviderDisplayNameMapper.Apply(results);
                return results;
            },
            unavailableValue).ConfigureAwait(false);
    }

    private SqliteConnection CreateReadConnection()
    {
        return this._connectionFactory.CreateReadConnection();
    }
}
