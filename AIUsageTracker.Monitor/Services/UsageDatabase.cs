// <copyright file="UsageDatabase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public class UsageDatabase : IUsageDatabase
{
    private static readonly TimeSpan DetailFadeWindow = TimeSpan.FromDays(7);
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly ILogger<UsageDatabase> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public UsageDatabase(ILogger<UsageDatabase> logger, IAppPathProvider pathProvider)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;
        this._dbPath = this._pathProvider.GetDatabasePath();

        var dbDir = Path.GetDirectoryName(this._dbPath);
        if (!string.IsNullOrEmpty(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        this._logger.LogInformation("Database path: {DbPath}", this._dbPath);

        this._connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this._dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 15,
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => this.RunMigrations()).ConfigureAwait(false);
    }

    private void RunMigrations()
    {
        var migrationService = new DatabaseMigrationService(
            this._dbPath,
            LoggerFactory.Create(builder => builder.AddProvider(new LoggerProvider(this._logger))).CreateLogger<DatabaseMigrationService>());
        migrationService.RunMigrations();
    }

    private class LoggerProvider : ILoggerProvider
    {
        private readonly ILogger _logger;

        public LoggerProvider(ILogger logger) => this._logger = logger;

        public ILogger CreateLogger(string categoryName) => this._logger;

        public void Dispose()
        {
        }
    }

    public async Task StoreProviderAsync(ProviderConfig config, string? friendlyName = null)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                INSERT INTO providers (
                    provider_id, provider_name, auth_source, account_name, created_at, updated_at, is_active, config_json
                ) VALUES (
                    @ProviderId, @ProviderName, @AuthSource, @AccountName, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1, @ConfigJson
                )
                ON CONFLICT(provider_id) DO UPDATE SET
                    provider_name = excluded.provider_name,
                    auth_source = excluded.auth_source,
                    account_name = CASE
                        WHEN excluded.account_name IS NOT NULL AND excluded.account_name != '' THEN excluded.account_name
                        ELSE providers.account_name
                    END,
                    updated_at = CURRENT_TIMESTAMP,
                    config_json = excluded.config_json,
                    is_active = 1";

            var safeConfig = new
            {
                config.ProviderId,
                config.Type,
                config.AuthSource,
            };

            await connection.ExecuteAsync(sql, new
            {
                ProviderId = config.ProviderId,
                ProviderName = friendlyName ?? config.ProviderId,
                AuthSource = config.AuthSource ?? "manual",
                AccountName = (string?)null,
                ConfigJson = JsonSerializer.Serialize(safeConfig),
            }).ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task StoreHistoryAsync(IEnumerable<ProviderUsage> usages)
    {
        var validUsages = usages
            .Where(u => !string.IsNullOrWhiteSpace(u.ProviderId))
            .ToList();

        if (!validUsages.Any())
        {
            return;
        }

        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string providerUpsertSql = @"
                INSERT INTO providers (
                    provider_id, provider_name, auth_source,
                    account_name, updated_at, is_active, config_json
                ) VALUES (
                    @ProviderId, @ProviderName, @AuthSource,
                    @AccountName, CURRENT_TIMESTAMP, @IsActive, '{}'
                )
                ON CONFLICT(provider_id) DO UPDATE SET
                    provider_name = CASE
                        WHEN excluded.provider_name IS NOT NULL AND excluded.provider_name != '' THEN excluded.provider_name
                        ELSE providers.provider_name
                    END,
                    auth_source = CASE
                        WHEN excluded.auth_source IS NOT NULL AND excluded.auth_source != '' THEN excluded.auth_source
                        ELSE providers.auth_source
                    END,
                    account_name = CASE
                        WHEN excluded.account_name IS NOT NULL AND excluded.account_name != '' THEN excluded.account_name
                        ELSE providers.account_name
                    END,
                    is_active = excluded.is_active,
                    updated_at = CURRENT_TIMESTAMP";

            const string sql = @"
                INSERT INTO provider_history (
                    provider_id,
                    requests_used, requests_available, requests_percentage,
                    is_available, status_message, next_reset_time, fetched_at,
                    details_json, response_latency_ms, http_status,
                    upstream_response_validity, upstream_response_note
                ) VALUES (
                    @ProviderId,
                    @RequestsUsed, @RequestsAvailable, @RequestsPercentage,
                    @IsAvailable, @StatusMessage, @NextResetTime, @FetchedAt,
                    @DetailsJson, @ResponseLatencyMs, @HttpStatus,
                    @UpstreamResponseValidity, @UpstreamResponseNote
                )";

            var providerUpsertParameters = validUsages.Select(u => new
            {
                ProviderId = u.ProviderId,
                ProviderName = u.ProviderName,
                AuthSource = u.AuthSource,
                AccountName = u.AccountName,
                IsActive = u.IsAvailable ? 1 : 0,
            });

            await connection.ExecuteAsync(providerUpsertSql, providerUpsertParameters).ConfigureAwait(false);

            var parameters = validUsages.Select(u => new
            {
                ProviderId = u.ProviderId,
                RequestsUsed = u.RequestsUsed,
                RequestsAvailable = u.RequestsAvailable,
                RequestsPercentage = u.RequestsPercentage,
                IsAvailable = u.IsAvailable ? 1 : 0,
                StatusMessage = u.Description ?? string.Empty,
                NextResetTime = u.NextResetTime?.ToString("O"),
                FetchedAt = (u.FetchedAt == default ? DateTime.UtcNow : u.FetchedAt).ToString("O"),
                DetailsJson = u.Details != null && u.Details.Any()
                    ? JsonSerializer.Serialize(u.Details)
                    : null,
                ResponseLatencyMs = u.ResponseLatencyMs,
                HttpStatus = u.HttpStatus,
                UpstreamResponseValidity = (int)(u.UpstreamResponseValidity == UpstreamResponseValidity.Unknown
                    ? UpstreamResponseValidityCatalog.Evaluate(u).Validity
                    : u.UpstreamResponseValidity),
                UpstreamResponseNote = string.IsNullOrWhiteSpace(u.UpstreamResponseNote)
                    ? UpstreamResponseValidityCatalog.Evaluate(u).Note
                    : u.UpstreamResponseNote,
            });

            await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task StoreRawSnapshotAsync(string providerId, string rawJson, int httpStatus)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                INSERT INTO raw_snapshots (provider_id, raw_json, http_status, fetched_at)
                VALUES (@ProviderId, @RawJson, @HttpStatus, CURRENT_TIMESTAMP)";

            await connection.ExecuteAsync(sql, new { ProviderId = providerId, RawJson = rawJson, HttpStatus = httpStatus }).ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task CleanupOldSnapshotsAsync()
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "DELETE FROM raw_snapshots WHERE fetched_at < datetime('now', '-7 days')";
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task OptimizeAsync()
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await connection.ExecuteAsync("PRAGMA optimize").ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task StoreResetEventAsync(string providerId, string providerName,
        double? previousUsage, double? newUsage, string resetType)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                INSERT INTO reset_events (
                    provider_id, provider_name, previous_usage, new_usage, reset_type, timestamp
                ) VALUES (
                    @ProviderId, @ProviderName, @PreviousUsage, @NewUsage, @ResetType, CURRENT_TIMESTAMP
                )";

            await connection.ExecuteAsync(sql, new
            {
                ProviderId = providerId,
                ProviderName = providerName,
                PreviousUsage = previousUsage,
                NewUsage = newUsage,
                ResetType = resetType,
            }).ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetLatestHistoryAsync()
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT h.provider_id AS ProviderId,
                       COALESCE(NULLIF(p.provider_name, ''), h.provider_id) AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                       h.status_message AS Description, h.fetched_at AS FetchedAt,
                       h.next_reset_time AS NextResetTime, h.details_json AS DetailsJson,
                       h.response_latency_ms AS ResponseLatencyMs,
                       h.http_status AS HttpStatus,
                       COALESCE(h.upstream_response_validity, 0) AS UpstreamResponseValidity,
                       COALESCE(h.upstream_response_note, '') AS UpstreamResponseNote,
                       COALESCE(p.account_name, '') AS AccountName,
                       COALESCE(p.auth_source, '') AS AuthSource
                FROM provider_history h
                LEFT JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.id IN (
                    SELECT MAX(id) FROM provider_history GROUP BY provider_id
                )
                ORDER BY h.provider_id";

            var results = (await connection.QueryAsync<ProviderUsage>(sql).ConfigureAwait(false)).ToList();

            foreach (var usage in results.Where(u => !string.IsNullOrWhiteSpace(u.DetailsJson)))
            {
                try
                {
                    usage.Details = JsonSerializer.Deserialize<List<ProviderUsageDetail>>(usage.DetailsJson!);
                }
                catch (JsonException ex)
                {
                    this._logger.LogWarning(ex, "Failed to parse details_json for provider {ProviderId}", usage.ProviderId);
                }
            }

            await this.MergeRecentlySeenDetailsAsync(connection, results, DateTime.UtcNow - DetailFadeWindow).ConfigureAwait(false);

            foreach (var usage in results)
            {
                if (ProviderMetadataCatalog.TryGet(usage.ProviderId, out var definition))
                {
                    usage.PlanType = definition.PlanType;
                    usage.IsQuotaBased = definition.IsQuotaBased;

                    var mappedName = definition.ResolveDisplayName(usage.ProviderId);
                    if (!string.IsNullOrWhiteSpace(mappedName))
                    {
                        usage.ProviderName = mappedName;
                    }
                }

                if (!usage.DisplayAsFraction && usage.IsQuotaBased && usage.RequestsAvailable > 100)
                {
                    usage.DisplayAsFraction = true;
                }

                ApplyUpstreamResponseValidity(usage);
            }

            return results;
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    private async Task MergeRecentlySeenDetailsAsync(
        SqliteConnection connection,
        IReadOnlyCollection<ProviderUsage> latestUsages,
        DateTime cutoffUtc)
    {
        if (latestUsages.Count == 0)
        {
            return;
        }

        const string sql = @"
                SELECT provider_id AS ProviderId,
                       details_json AS DetailsJson,
                       fetched_at AS FetchedAt
                FROM provider_history
                WHERE fetched_at >= @CutoffUtc
                  AND details_json IS NOT NULL
                  AND details_json != ''
                ORDER BY provider_id, fetched_at DESC";

        var rows = await connection.QueryAsync<RecentProviderDetailsRow>(sql, new
        {
            CutoffUtc = cutoffUtc.ToString("O"),
        }).ConfigureAwait(false);

        var latestByProvider = latestUsages.ToDictionary(
            usage => usage.ProviderId,
            StringComparer.OrdinalIgnoreCase);

        var recentByProvider = new Dictionary<string, Dictionary<string, RecentDetailSnapshot>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ProviderId) || string.IsNullOrWhiteSpace(row.DetailsJson))
            {
                continue;
            }

            List<ProviderUsageDetail>? parsedDetails;
            try
            {
                parsedDetails = JsonSerializer.Deserialize<List<ProviderUsageDetail>>(row.DetailsJson);
            }
            catch (JsonException ex)
            {
                this._logger.LogWarning(ex, "Failed to parse historical details_json for provider {ProviderId}", row.ProviderId);
                continue;
            }

            if (parsedDetails == null || parsedDetails.Count == 0)
            {
                continue;
            }

            if (!DateTime.TryParse(row.FetchedAt, out var parsedFetchedAt))
            {
                continue;
            }

            var fetchedAtUtc = parsedFetchedAt.ToUniversalTime();
            if (fetchedAtUtc < cutoffUtc)
            {
                continue;
            }

            if (!recentByProvider.TryGetValue(row.ProviderId, out var detailMap))
            {
                detailMap = new Dictionary<string, RecentDetailSnapshot>(StringComparer.OrdinalIgnoreCase);
                recentByProvider[row.ProviderId] = detailMap;
            }

            foreach (var detail in parsedDetails)
            {
                if (detail == null || string.IsNullOrWhiteSpace(detail.Name))
                {
                    continue;
                }

                var key = BuildDetailMergeKey(detail);
                if (!detailMap.ContainsKey(key))
                {
                    detailMap[key] = new RecentDetailSnapshot(detail, fetchedAtUtc);
                }
            }
        }

        foreach (var usage in latestByProvider.Values)
        {
            if (!recentByProvider.TryGetValue(usage.ProviderId, out var recentDetails) || recentDetails.Count == 0)
            {
                continue;
            }

            var currentDetails = usage.Details?.ToList() ?? new List<ProviderUsageDetail>();
            var currentKeys = currentDetails
                .Where(detail => detail != null)
                .Select(BuildDetailMergeKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allowedDetailTypes = currentDetails
                .Where(detail => detail != null)
                .Select(detail => detail.DetailType)
                .Distinct()
                .ToHashSet();

            foreach (var snapshot in recentDetails.Values.OrderByDescending(x => x.FetchedAtUtc))
            {
                if (allowedDetailTypes.Count > 0 &&
                    !allowedDetailTypes.Contains(snapshot.Detail.DetailType))
                {
                    continue;
                }

                var key = BuildDetailMergeKey(snapshot.Detail);
                if (currentKeys.Contains(key))
                {
                    continue;
                }

                var staleDetail = CloneDetail(snapshot.Detail);
                staleDetail.Description = AppendStaleSuffix(staleDetail.Description, snapshot.FetchedAtUtc);
                currentDetails.Add(staleDetail);
                currentKeys.Add(key);
            }

            usage.Details = currentDetails;
            if (!usage.NextResetTime.HasValue)
            {
                usage.NextResetTime = InferNextResetFromDetails(currentDetails);
            }
        }
    }

    private static DateTime? InferNextResetFromDetails(IReadOnlyList<ProviderUsageDetail> details)
    {
        if (details.Count == 0)
        {
            return null;
        }

        var nowUtc = DateTime.UtcNow;
        DateTime? bestFuture = null;
        DateTime? lastKnown = null;
        foreach (var detail in details)
        {
            if (!detail.NextResetTime.HasValue)
            {
                continue;
            }

            var resetUtc = detail.NextResetTime.Value.ToUniversalTime();
            if (!lastKnown.HasValue || resetUtc > lastKnown.Value)
            {
                lastKnown = resetUtc;
            }

            if (resetUtc > nowUtc && (!bestFuture.HasValue || resetUtc < bestFuture.Value))
            {
                bestFuture = resetUtc;
            }
        }

        return bestFuture ?? lastKnown;
    }

    private static string BuildDetailMergeKey(ProviderUsageDetail detail)
    {
        return $"{detail.DetailType}|{detail.QuotaBucketKind}|{detail.Name.Trim()}|{detail.ModelName.Trim()}|{detail.GroupName.Trim()}";
    }

    private static ProviderUsageDetail CloneDetail(ProviderUsageDetail source)
    {
        return new ProviderUsageDetail
        {
            Name = source.Name,
            ModelName = source.ModelName,
            GroupName = source.GroupName,
            Used = source.Used,
            Description = source.Description,
            NextResetTime = source.NextResetTime,
            DetailType = source.DetailType,
            QuotaBucketKind = source.QuotaBucketKind,
        };
    }

    private static string AppendStaleSuffix(string description, DateTime lastSeenUtc)
    {
        var baseDescription = description ?? string.Empty;
        var staleSuffix = $"(stale; last seen {lastSeenUtc:yyyy-MM-dd})";
        return string.IsNullOrWhiteSpace(baseDescription)
            ? staleSuffix
            : $"{baseDescription} {staleSuffix}";
    }

    private static void ApplyUpstreamResponseValidity(ProviderUsage usage)
    {
        var evaluation = UpstreamResponseValidityCatalog.Evaluate(usage);
        usage.UpstreamResponseValidity = evaluation.Validity;
        usage.UpstreamResponseNote = evaluation.Note;
    }

    private sealed record RecentProviderDetailsRow(string ProviderId, string DetailsJson, string FetchedAt);

    private sealed record RecentDetailSnapshot(ProviderUsageDetail Detail, DateTime FetchedAtUtc);

    public async Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var sql = $@"
                SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                       h.status_message AS Description, h.fetched_at AS FetchedAt,
                       h.next_reset_time AS NextResetTime,
                       h.details_json AS DetailsJson,
                       h.response_latency_ms AS ResponseLatencyMs,
                       h.http_status AS HttpStatus,
                       COALESCE(h.upstream_response_validity, 0) AS UpstreamResponseValidity,
                       COALESCE(h.upstream_response_note, '') AS UpstreamResponseNote
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

            var results = (await connection.QueryAsync<ProviderUsage>(sql).ConfigureAwait(false)).ToList();

            foreach (var usage in results.Where(u => !string.IsNullOrWhiteSpace(u.DetailsJson)))
            {
                try
                {
                    usage.Details = JsonSerializer.Deserialize<List<ProviderUsageDetail>>(usage.DetailsJson!);
                }
                catch (JsonException ex)
                {
                    this._logger.LogWarning(ex, "Failed to parse details_json for provider {ProviderId}", usage.ProviderId);
                }
            }

            foreach (var usage in results)
            {
                ApplyUpstreamResponseValidity(usage);
            }

            return results;
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var sql = $@"
                SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                       h.status_message AS Description, h.fetched_at AS FetchedAt,
                       h.next_reset_time AS NextResetTime,
                       h.details_json AS DetailsJson,
                       h.response_latency_ms AS ResponseLatencyMs,
                       h.http_status AS HttpStatus,
                       COALESCE(h.upstream_response_validity, 0) AS UpstreamResponseValidity,
                       COALESCE(h.upstream_response_note, '') AS UpstreamResponseNote
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.provider_id = @ProviderId
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

            var results = (await connection.QueryAsync<ProviderUsage>(sql, new { ProviderId = providerId }).ConfigureAwait(false)).ToList();

            foreach (var usage in results.Where(u => !string.IsNullOrWhiteSpace(u.DetailsJson)))
            {
                try
                {
                    usage.Details = JsonSerializer.Deserialize<List<ProviderUsageDetail>>(usage.DetailsJson!);
                }
                catch (JsonException ex)
                {
                    this._logger.LogWarning(ex, "Failed to parse details_json for provider {ProviderId}", usage.ProviderId);
                }
            }

            foreach (var usage in results)
            {
                ApplyUpstreamResponseValidity(usage);
            }

            return results;
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetRecentHistoryAsync(int countPerProvider)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var sql = $@"
                WITH RankedHistory AS (
                    SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                           h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                           h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                           h.status_message AS Description, h.fetched_at AS FetchedAt,
                           h.next_reset_time AS NextResetTime,
                           h.details_json AS DetailsJson,
                           h.response_latency_ms AS ResponseLatencyMs,
                           h.http_status AS HttpStatus,
                           COALESCE(h.upstream_response_validity, 0) AS UpstreamResponseValidity,
                           COALESCE(h.upstream_response_note, '') AS UpstreamResponseNote,
                           ROW_NUMBER() OVER (PARTITION BY h.provider_id ORDER BY h.fetched_at DESC) as pos
                    FROM provider_history h
                    JOIN providers p ON h.provider_id = p.provider_id
                )
                SELECT ProviderId, ProviderName, RequestsUsed, RequestsAvailable,
                       RequestsPercentage, IsAvailable, Description, FetchedAt, NextResetTime,
                       DetailsJson, ResponseLatencyMs, HttpStatus, UpstreamResponseValidity, UpstreamResponseNote
                FROM RankedHistory
                WHERE pos <= @Count
                ORDER BY ProviderId, FetchedAt DESC";

            var results = (await connection.QueryAsync<ProviderUsage>(sql, new { Count = countPerProvider }).ConfigureAwait(false)).ToList();

            foreach (var usage in results.Where(u => !string.IsNullOrWhiteSpace(u.DetailsJson)))
            {
                try
                {
                    usage.Details = JsonSerializer.Deserialize<List<ProviderUsageDetail>>(usage.DetailsJson!);
                }
                catch (JsonException ex)
                {
                    this._logger.LogWarning(ex, "Failed to parse details_json for provider {ProviderId}", usage.ProviderId);
                }
            }

            foreach (var usage in results)
            {
                ApplyUpstreamResponseValidity(usage);
            }

            return results;
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var sql = $@"
                SELECT id AS Id, provider_id AS ProviderId, provider_name AS ProviderName,
                       previous_usage AS PreviousUsage, new_usage AS NewUsage,
                       reset_type AS ResetType, timestamp AS Timestamp
                FROM reset_events
                WHERE provider_id = @ProviderId
                ORDER BY timestamp DESC
                LIMIT {limit}";

            var results = await connection.QueryAsync<ResetEvent>(sql, new { ProviderId = providerId }).ConfigureAwait(false);
            return results.ToList();
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task<bool> IsHistoryEmptyAsync()
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM provider_history").ConfigureAwait(false);
            return count == 0;
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task SetProviderActiveAsync(string providerId, bool isActive)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(this._connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await connection.ExecuteAsync(
                "UPDATE providers SET is_active = @IsActive, updated_at = CURRENT_TIMESTAMP WHERE provider_id = @ProviderId",
                new { ProviderId = providerId, IsActive = isActive ? 1 : 0 }).ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }
}

