// <copyright file="UsageDatabase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Data;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public class UsageDatabase : IUsageDatabase
{
    /// <summary>
    /// Rows older than this are flagged as stale so the UI can warn the user
    /// that the data may not reflect the current provider state.
    ///
    /// This is intentionally set to 2× the circuit-breaker maximum backoff
    /// (see <see cref="ProviderRefreshCircuitBreakerService"/> — CircuitBreakerMaxBackoff = 30 min).
    /// A row older than 1 hour means at least two full retry windows have elapsed
    /// without a successful refresh, either because repeated errors have kept the
    /// circuit open at max backoff or because the monitor process was not running.
    /// Changing CircuitBreakerMaxBackoff should be accompanied by a matching update here.
    /// </summary>
    private static readonly TimeSpan StaleDataThreshold = TimeSpan.FromHours(1);

    // Compaction runs at most once per day. DateTime.MinValue means "never run" — compaction fires on first startup.
    private DateTime _lastCompactedAt = DateTime.MinValue;

    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly ILogger<UsageDatabase> _logger;
    private readonly IAppPathProvider _pathProvider;
    // Serialize writes/maintenance only. Read operations use independent connections so
    // SQLite WAL can serve them concurrently with refresh writes.
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

    /// <summary>
    /// Registers Dapper type handlers once per process. All DateTime values read from
    /// the database are tagged Kind=Utc, matching the storage convention.
    /// </summary>
    static UsageDatabase()
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
        SqlMapper.AddTypeHandler(new WindowKindHandler());
    }

    /// <summary>
    /// Dapper type handler that ensures all DateTime values read from SQLite are
    /// interpreted as UTC. SQLite stores DateTime as TEXT and Dapper returns Kind=Unspecified
    /// by default; this handler corrects that to Kind=Utc.
    /// </summary>
    private sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.Value = value.Kind == DateTimeKind.Utc
                ? value.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                : value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        public override DateTime Parse(object value)
        {
            return value switch
            {
                DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed) => parsed,
                string s => DateTime.SpecifyKind(DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
                _ => DateTime.SpecifyKind(Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
            };
        }
    }

    /// <summary>
    /// Dapper type handler that maps the INTEGER <c>window_kind</c> column to the
    /// <see cref="WindowKind"/> enum. SQLite stores the value as a long; this handler
    /// converts it to the correct enum value on read and stores it as an int on write.
    /// </summary>
    private sealed class WindowKindHandler : SqlMapper.TypeHandler<WindowKind>
    {
        public override void SetValue(IDbDataParameter parameter, WindowKind value)
        {
            parameter.Value = (int)value;
        }

        public override WindowKind Parse(object value)
        {
            return (WindowKind)Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON").ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenReadConnectionAsync()
    {
        var connection = new SqliteConnection(this._connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private async Task<SqliteConnection> OpenWriteConnectionAsync(bool enableForeignKeys = false)
    {
        var connection = new SqliteConnection(this._connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        if (enableForeignKeys)
        {
            await EnableForeignKeysAsync(connection).ConfigureAwait(false);
        }

        return connection;
    }

    public async Task StoreProviderAsync(ProviderConfig config, string? friendlyName = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = await this.OpenWriteConnectionAsync(enableForeignKeys: true).ConfigureAwait(false);

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
                config.AuthSource,
            };

            await connection.ExecuteAsync(sql, new
            {
                ProviderId = config.ProviderId,
                ProviderName = friendlyName ?? config.ProviderId,
                AuthSource = config.AuthSource ?? "manual",
                AccountName = default(string),
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
            using var connection = await this.OpenWriteConnectionAsync(enableForeignKeys: true).ConfigureAwait(false);

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

            await connection.ExecuteAsync(providerUpsertSql, validUsages.Select(u => new
            {
                ProviderId = u.ProviderId,
                ProviderName = u.ProviderName,
                AuthSource = u.AuthSource,
                AccountName = u.AccountName,
                IsActive = u.IsAvailable ? 1 : 0,
            })).ConfigureAwait(false);

            // Dedup gate: load the last stored row per (provider_id, card_id) and only INSERT when
            // something meaningful has changed. When data is unchanged, we UPDATE the
            // existing row's fetched_at so the stale-data detector keeps seeing a fresh
            // timestamp even though no new row was written.
            var providerIds = validUsages.Select(u => u.ProviderId!).Distinct().ToList();
            var lastRows = await LoadLastHistoryRowsAsync(connection, providerIds).ConfigureAwait(false);

            var toInsert = new List<HistoryInsertParams>();
            var toTouch = new List<HistoryTouchParams>();

            foreach (var u in validUsages)
            {
                var fetchedAt = ToUnixEpoch(u.FetchedAt == default ? DateTime.UtcNow : u.FetchedAt);
                var nextResetTime = u.NextResetTime?.ToString("O");
                var statusMessage = u.Description ?? string.Empty;
                var validityEval = u.EvaluateUpstreamResponseValidity();
                var validityInt = (int)(u.UpstreamResponseValidity == UpstreamResponseValidity.Unknown
                    ? validityEval.Validity
                    : u.UpstreamResponseValidity);
                var validityNote = string.IsNullOrWhiteSpace(u.UpstreamResponseNote)
                    ? validityEval.Note
                    : u.UpstreamResponseNote;

                // Composite dedup key: provider_id + card_id (null card_id = legacy single-card provider)
                var dedupKey = $"{u.ProviderId!}::{u.CardId ?? string.Empty}";
                if (lastRows.TryGetValue(dedupKey, out var last)
                    && IsHistoryUnchanged(u, last, nextResetTime, statusMessage))
                {
                    toTouch.Add(new HistoryTouchParams(last.Id, fetchedAt));
                }
                else
                {
                    toInsert.Add(new HistoryInsertParams(
                        u.ProviderId!,
                        u.RequestsUsed,
                        u.RequestsAvailable,
                        u.UsedPercent,
                        u.IsAvailable ? 1 : 0,
                        statusMessage,
                        nextResetTime,
                        fetchedAt,
                        u.ResponseLatencyMs,
                        u.HttpStatus,
                        validityInt,
                        validityNote,
                        u.ParentProviderId,
                        u.CardId,
                        u.GroupId,
                        (int)u.WindowKind,
                        u.ModelName,
                        u.Name));
                }
            }

            if (toInsert.Count > 0)
            {
                const string insertSql = @"
                    INSERT INTO provider_history (
                        provider_id,
                        requests_used, requests_available, requests_percentage,
                        is_available, status_message, next_reset_time, fetched_at,
                        response_latency_ms, http_status,
                        upstream_response_validity, upstream_response_note,
                        parent_provider_id, card_id, group_id,
                        window_kind, model_name, name
                    ) VALUES (
                        @ProviderId,
                        @RequestsUsed, @RequestsAvailable, @RequestsPercentage,
                        @IsAvailable, @StatusMessage, @NextResetTime, @FetchedAt,
                        @ResponseLatencyMs, @HttpStatus,
                        @UpstreamResponseValidity, @UpstreamResponseNote,
                        @ParentProviderId, @CardId, @GroupId,
                        @WindowKind, @ModelName, @Name
                    )";

                await connection.ExecuteAsync(insertSql, toInsert).ConfigureAwait(false);
            }

            if (toTouch.Count > 0)
            {
                await connection.ExecuteAsync(
                    "UPDATE provider_history SET fetched_at = @FetchedAt WHERE id = @Id",
                    toTouch).ConfigureAwait(false);
            }

            if (toInsert.Count > 0 || toTouch.Count > 0)
            {
                this._logger.LogDebug(
                    "History write: {Inserted} inserted, {Touched} touched (data unchanged)",
                    toInsert.Count,
                    toTouch.Count);
            }
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    private static bool IsHistoryUnchanged(
        ProviderUsage usage,
        LastHistoryRow last,
        string? newNextResetTime,
        string newStatusMessage)
    {
        return Math.Abs(usage.RequestsUsed - last.RequestsUsed) < 0.001
            && Math.Abs(usage.RequestsAvailable - last.RequestsAvailable) < 0.001
            && (usage.IsAvailable ? 1L : 0L) == last.IsAvailable
            && (long)usage.HttpStatus == last.HttpStatus
            && string.Equals(newStatusMessage, last.StatusMessage ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(newNextResetTime, last.NextResetTime, StringComparison.Ordinal);
    }

    private static async Task<Dictionary<string, LastHistoryRow>> LoadLastHistoryRowsAsync(
        SqliteConnection connection,
        IList<string> providerIds)
    {
        if (providerIds.Count == 0)
        {
            return new Dictionary<string, LastHistoryRow>(StringComparer.OrdinalIgnoreCase);
        }

        const string sql = @"
            SELECT h.id AS Id,
                   h.provider_id AS ProviderId,
                   h.card_id AS CardId,
                   h.requests_used AS RequestsUsed,
                   h.requests_available AS RequestsAvailable,
                   h.is_available AS IsAvailable,
                   h.status_message AS StatusMessage,
                   h.next_reset_time AS NextResetTime,
                   h.http_status AS HttpStatus
            FROM provider_history h
            WHERE h.id IN (
                SELECT MAX(id)
                FROM provider_history
                WHERE provider_id IN @Ids
                GROUP BY provider_id, card_id
            )";

        var rows = await connection.QueryAsync<LastHistoryRow>(sql, new { Ids = providerIds }).ConfigureAwait(false);
        return rows.ToDictionary(
            r => $"{r.ProviderId}::{r.CardId ?? string.Empty}",
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed record LastHistoryRow(
        long Id,
        string ProviderId,
        string? CardId,
        double RequestsUsed,
        double RequestsAvailable,
        long IsAvailable,
        string? StatusMessage,
        string? NextResetTime,
        long HttpStatus);

    private sealed record HistoryInsertParams(
        string ProviderId,
        double RequestsUsed,
        double RequestsAvailable,
        double RequestsPercentage,
        int IsAvailable,
        string StatusMessage,
        string? NextResetTime,
        long FetchedAt,
        double ResponseLatencyMs,
        int HttpStatus,
        int UpstreamResponseValidity,
        string UpstreamResponseNote,
        string? ParentProviderId,
        string? CardId,
        string? GroupId,
        int WindowKind,
        string? ModelName,
        string? Name);

    private sealed record HistoryTouchParams(long Id, long FetchedAt);

    private static long ToUnixEpoch(DateTime dt) =>
        new DateTimeOffset(dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();

    public async Task StoreRawSnapshotAsync(string providerId, string rawJson, int httpStatus)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = await this.OpenWriteConnectionAsync(enableForeignKeys: true).ConfigureAwait(false);

            const string sql = @"
                INSERT INTO raw_snapshots (provider_id, raw_json, http_status, fetched_at)
                VALUES (@ProviderId, @RawJson, @HttpStatus, strftime('%s', 'now'))";

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
            using var connection = await this.OpenWriteConnectionAsync().ConfigureAwait(false);

            const string sql = "DELETE FROM raw_snapshots WHERE fetched_at < (strftime('%s', 'now') - 604800)";
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task CompactHistoryAsync()
    {
        var now = DateTime.UtcNow;
        if (now - this._lastCompactedAt < TimeSpan.FromHours(23))
        {
            return;
        }

        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = await this.OpenWriteConnectionAsync().ConfigureAwait(false);

            // Phase 1: rows in the 7–90 day window → keep last row per (provider, hour)
            const string downsampleHourly = @"
                DELETE FROM provider_history
                WHERE id NOT IN (
                    SELECT MAX(id)
                    FROM provider_history
                    WHERE fetched_at >= (strftime('%s', 'now') - 7776000)
                      AND fetched_at < (strftime('%s', 'now') - 604800)
                    GROUP BY provider_id, strftime('%Y-%m-%dT%H', fetched_at, 'unixepoch')
                )
                AND fetched_at >= (strftime('%s', 'now') - 7776000)
                AND fetched_at < (strftime('%s', 'now') - 604800)";

            // Phase 2: rows older than 90 days → keep last row per (provider, day)
            const string downsampleDaily = @"
                DELETE FROM provider_history
                WHERE id NOT IN (
                    SELECT MAX(id)
                    FROM provider_history
                    WHERE fetched_at < (strftime('%s', 'now') - 7776000)
                    GROUP BY provider_id, date(fetched_at, 'unixepoch')
                )
                AND fetched_at < (strftime('%s', 'now') - 7776000)";

            var deletedHourly = await connection.ExecuteAsync(downsampleHourly).ConfigureAwait(false);
            var deletedDaily = await connection.ExecuteAsync(downsampleDaily).ConfigureAwait(false);
            var totalDeleted = deletedHourly + deletedDaily;

            if (totalDeleted > 0)
            {
                this._logger.LogInformation(
                    "History compacted: removed {Total} rows ({Hourly} from 7–90d window, {Daily} from >90d window). Running VACUUM.",
                    totalDeleted,
                    deletedHourly,
                    deletedDaily);

                // VACUUM must run outside a transaction and requires a separate connection.
                await connection.CloseAsync().ConfigureAwait(false);
                using var vacuumConnection = await this.OpenWriteConnectionAsync().ConfigureAwait(false);
                await vacuumConnection.ExecuteAsync("VACUUM").ConfigureAwait(false);
            }
            else
            {
                this._logger.LogDebug("History compaction: nothing to compact.");
            }

            this._lastCompactedAt = now;
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
            using var connection = await this.OpenWriteConnectionAsync().ConfigureAwait(false);
            await connection.ExecuteAsync("PRAGMA optimize").ConfigureAwait(false);
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    public async Task StoreResetEventAsync(
        string providerId,
        string providerName,
        double? previousUsage,
        double? newUsage,
        string resetType)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = await this.OpenWriteConnectionAsync(enableForeignKeys: true).ConfigureAwait(false);

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

    public async Task<IReadOnlyList<ProviderUsage>> GetLatestHistoryAsync(IReadOnlyCollection<string>? providerIds = null)
    {
        if (providerIds != null && providerIds.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        using var connection = await this.OpenReadConnectionAsync().ConfigureAwait(false);

        // When a provider-ID set is supplied, restrict the subquery to those IDs so that
        // stale history rows for removed/unconfigured providers are excluded at the SQL level
        // rather than filtered in application code.
        var providerClause = providerIds != null ? "AND provider_id IN @providerIds" : string.Empty;

        var sql = $@"
                SELECT h.provider_id AS ProviderId,
                       COALESCE(NULLIF(p.provider_name, ''), h.provider_id) AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS UsedPercent, h.is_available AS IsAvailable,
                       h.status_message AS Description, strftime('%Y-%m-%dT%H:%M:%SZ', h.fetched_at, 'unixepoch') AS FetchedAt,
                       h.next_reset_time AS NextResetTime,
                       h.response_latency_ms AS ResponseLatencyMs,
                       h.http_status AS HttpStatus,
                       COALESCE(h.upstream_response_validity, 0) AS UpstreamResponseValidity,
                       COALESCE(h.upstream_response_note, '') AS UpstreamResponseNote,
                       COALESCE(p.account_name, '') AS AccountName,
                       COALESCE(p.auth_source, '') AS AuthSource,
                       h.parent_provider_id AS ParentProviderId,
                       h.card_id AS CardId,
                       h.group_id AS GroupId,
                       COALESCE(h.window_kind, 0) AS WindowKind,
                       h.model_name AS ModelName,
                       h.name AS Name
                FROM provider_history h
                LEFT JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.id IN (
                    SELECT MAX(id) FROM provider_history
                    WHERE fetched_at >= (strftime('%s', 'now') - 86400)
                    {providerClause}
                    GROUP BY provider_id, card_id
                )
                ORDER BY h.provider_id, h.card_id";

        object? param = providerIds != null ? new { providerIds = providerIds.ToArray() } : null;
        var results = (await connection.QueryAsync<ProviderUsage>(sql, param).ConfigureAwait(false)).ToList();

        await StampUsageRatesAsync(connection, results).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        foreach (var usage in results)
        {
            var usageDef = ProviderMetadataCatalog.Find(usage.ProviderId ?? string.Empty);
            if (usageDef != null)
            {
                usage.PlanType = usageDef.PlanType;
                usage.IsQuotaBased = usageDef.IsQuotaBased;

                // provider-id-guardrail-allow: this comment explains matching against runtime provider ids
                // Re-derive WindowKind from the provider's QuotaWindowDefinition so stale DB
                // entries are corrected without a DB migration.
                var childId = string.Equals(usage.ProviderId, usageDef.ProviderId, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(usage.CardId)
                        ? $"{usageDef.ProviderId}.{usage.CardId}"
                        : usage.ProviderId;

                var windowMatch = usageDef.QuotaWindows.FirstOrDefault(w =>
                    !string.IsNullOrWhiteSpace(w.ChildProviderId) &&
                    string.Equals(w.ChildProviderId, childId, StringComparison.OrdinalIgnoreCase));

                if (windowMatch != null)
                {
                    usage.WindowKind = windowMatch.Kind;
                }
            }

            usage.ProviderName = ProviderMetadataCatalog.ResolveDisplayLabel(usage.ProviderId ?? string.Empty, usage.ProviderName);

            ApplyUpstreamResponseValidity(usage);
            MarkStaleIfOutdated(usage, now);
        }

        return results;
    }

    /// <summary>
    /// Computes a per-provider burn rate (requests/hour) from historical rows and stamps
    /// it onto the in-memory <see cref="ProviderUsage.UsagePerHour"/> property.
    /// Uses the row closest to one hour ago (within a 30–120 min window) as the baseline.
    /// Returns null for a provider when there is insufficient history or the counter reset.
    /// </summary>
    private static async Task StampUsageRatesAsync(SqliteConnection connection, IReadOnlyList<ProviderUsage> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        // For each provider, find the row closest to 1 hour ago (within the 30 min–2 hr window).
        // With epoch integers, time arithmetic is simple integer math (seconds).
        const string sql = @"
            WITH ranked AS (
                SELECT provider_id,
                       requests_used,
                       fetched_at,
                       ABS((strftime('%s', 'now') - fetched_at) / 60 - 60) AS minutes_from_target,
                       ROW_NUMBER() OVER (
                           PARTITION BY provider_id
                           ORDER BY ABS((strftime('%s', 'now') - fetched_at) / 60 - 60)
                       ) AS rn
                FROM provider_history
                WHERE fetched_at >= (strftime('%s', 'now') - 7200)
                  AND fetched_at <= (strftime('%s', 'now') - 1800)
            )
            SELECT provider_id AS ProviderId,
                   requests_used AS RequestsUsed,
                   fetched_at AS FetchedAt
            FROM ranked
            WHERE rn = 1";

        var baselines = (await connection.QueryAsync<(string ProviderId, double RequestsUsed, long FetchedAt)>(sql).ConfigureAwait(false))
            .ToDictionary(r => r.ProviderId, r => r, StringComparer.Ordinal);

        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var usage in results)
        {
            if (usage.ProviderId is null || !baselines.TryGetValue(usage.ProviderId, out var baseline))
            {
                continue;
            }

            var delta = usage.RequestsUsed - baseline.RequestsUsed;
            if (delta < 0)
            {
                // Counter reset — rate is undefined
                continue;
            }

            var hours = (nowEpoch - baseline.FetchedAt) / 3600.0;
            if (hours < 0.1)
            {
                continue;
            }

            usage.UsagePerHour = delta / hours;
        }
    }

    private static void ApplyUpstreamResponseValidity(ProviderUsage usage)
    {
        var evaluation = usage.EvaluateUpstreamResponseValidity();
        usage.UpstreamResponseValidity = evaluation.Validity;
        usage.UpstreamResponseNote = evaluation.Note;
    }

    private static void MarkStaleIfOutdated(ProviderUsage usage, DateTime now)
    {
        // Only flag available entries — unavailable/missing entries already carry an
        // explanatory description (e.g. "Temporarily paused", "Auth token not found"),
        // so adding a stale suffix on top would be redundant and confusing.
        if (!usage.IsAvailable)
        {
            return;
        }

        var fetchedAt = usage.FetchedAt.Kind == DateTimeKind.Utc
            ? usage.FetchedAt
            : usage.FetchedAt.ToUniversalTime();

        if (now - fetchedAt <= StaleDataThreshold)
        {
            return;
        }

        usage.IsStale = true;
        var age = now - fetchedAt;
        string ageLabel;
        if (age.TotalDays >= 1)
        {
            ageLabel = $"{(int)age.TotalDays}d ago";
        }
        else if (age.TotalHours >= 1)
        {
            ageLabel = $"{(int)age.TotalHours}h ago";
        }
        else
        {
            ageLabel = $"{(int)age.TotalMinutes}m ago";
        }
        var suffix = $"(last refreshed {ageLabel} — data may be outdated)";
        usage.Description = string.IsNullOrWhiteSpace(usage.Description)
            ? suffix
            : $"{usage.Description} {suffix}";
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 10_000);

        using var connection = await this.OpenReadConnectionAsync().ConfigureAwait(false);

        var sql = $@"
                SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS UsedPercent, h.is_available AS IsAvailable,
                       h.status_message AS Description, strftime('%Y-%m-%dT%H:%M:%SZ', h.fetched_at, 'unixepoch') AS FetchedAt,
                       h.next_reset_time AS NextResetTime,
                       h.response_latency_ms AS ResponseLatencyMs,
                       h.http_status AS HttpStatus,
                       COALESCE(h.upstream_response_validity, 0) AS UpstreamResponseValidity,
                       COALESCE(h.upstream_response_note, '') AS UpstreamResponseNote
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                ORDER BY h.fetched_at DESC
                LIMIT {limit}";

        var results = (await connection.QueryAsync<ProviderUsage>(sql).ConfigureAwait(false)).ToList();

        foreach (var usage in results)
        {
            ApplyUpstreamResponseValidity(usage);
        }

        return results;
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 10_000);

        using var connection = await this.OpenReadConnectionAsync().ConfigureAwait(false);

        var sql = $@"
                SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS UsedPercent, h.is_available AS IsAvailable,
                       h.status_message AS Description, strftime('%Y-%m-%dT%H:%M:%SZ', h.fetched_at, 'unixepoch') AS FetchedAt,
                       h.next_reset_time AS NextResetTime,
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

        foreach (var usage in results)
        {
            ApplyUpstreamResponseValidity(usage);
        }

        return results;
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetRecentHistoryAsync(int countPerProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(countPerProvider);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(countPerProvider, 10_000);

        using var connection = await this.OpenReadConnectionAsync().ConfigureAwait(false);

        var sql = $@"
                WITH RankedHistory AS (
                    SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                           h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                           h.requests_percentage AS UsedPercent, h.is_available AS IsAvailable,
                           h.status_message AS Description, strftime('%Y-%m-%dT%H:%M:%SZ', h.fetched_at, 'unixepoch') AS FetchedAt,
                           h.next_reset_time AS NextResetTime,
                           h.response_latency_ms AS ResponseLatencyMs,
                           h.http_status AS HttpStatus,
                           COALESCE(h.upstream_response_validity, 0) AS UpstreamResponseValidity,
                           COALESCE(h.upstream_response_note, '') AS UpstreamResponseNote,
                           ROW_NUMBER() OVER (PARTITION BY h.provider_id ORDER BY h.fetched_at DESC) as pos
                    FROM provider_history h
                    JOIN providers p ON h.provider_id = p.provider_id
                )
                SELECT ProviderId, ProviderName, RequestsUsed, RequestsAvailable,
                       UsedPercent, IsAvailable, Description, FetchedAt, NextResetTime,
                       ResponseLatencyMs, HttpStatus, UpstreamResponseValidity, UpstreamResponseNote
                FROM RankedHistory
                WHERE pos <= @Count
                ORDER BY ProviderId, FetchedAt DESC";

        var results = (await connection.QueryAsync<ProviderUsage>(sql, new { Count = countPerProvider }).ConfigureAwait(false)).ToList();

        foreach (var usage in results)
        {
            ApplyUpstreamResponseValidity(usage);
        }

        return results;
    }

    public async Task<IReadOnlyList<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        using var connection = await this.OpenReadConnectionAsync().ConfigureAwait(false);

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

    public async Task<bool> IsHistoryEmptyAsync()
    {
        using var connection = await this.OpenReadConnectionAsync().ConfigureAwait(false);
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM provider_history").ConfigureAwait(false);
        return count == 0;
    }

    public async Task SetProviderActiveAsync(string providerId, bool isActive)
    {
        await this._semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var connection = await this.OpenWriteConnectionAsync().ConfigureAwait(false);

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
