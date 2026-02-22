using System.Text.Json;
using AIConsumptionTracker.Core.Models;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Agent.Services;

public class UsageDatabase : IUsageDatabase
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly ILogger<UsageDatabase> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public UsageDatabase(ILogger<UsageDatabase> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");
        Directory.CreateDirectory(dbDir);
        _dbPath = Path.Combine(dbDir, "usage.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => RunMigrations());
    }

    private void RunMigrations()
    {
        var migrationService = new DatabaseMigrationService(_dbPath, 
            LoggerFactory.Create(builder => builder.AddProvider(new LoggerProvider(_logger))).CreateLogger<DatabaseMigrationService>());
        migrationService.RunMigrations();
    }

    private class LoggerProvider : ILoggerProvider
    {
        private readonly ILogger _logger;
        public LoggerProvider(ILogger logger) => _logger = logger;
        public ILogger CreateLogger(string categoryName) => _logger;
        public void Dispose() { }
    }

    public async Task StoreProviderAsync(ProviderConfig config, string? friendlyName = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT OR REPLACE INTO providers (
                    provider_id, provider_name, plan_type, auth_source, 
                    account_name, updated_at, is_active, config_json
                ) VALUES (
                    @ProviderId, @ProviderName, @PlanType, @AuthSource,
                    @AccountName, CURRENT_TIMESTAMP, @IsActive, @ConfigJson
                )";

            var safeConfig = new
            {
                config.ProviderId,
                config.Type,
                config.AuthSource
            };

            await connection.ExecuteAsync(sql, new
            {
                ProviderId = config.ProviderId,
                ProviderName = !string.IsNullOrEmpty(friendlyName) ? friendlyName : config.ProviderId,
                PlanType = config.PlanType.ToString().ToLower(),
                AuthSource = config.AuthSource ?? "manual",
                AccountName = (string?)null,
                IsActive = !string.IsNullOrEmpty(config.ApiKey) ? 1 : 0,
                ConfigJson = JsonSerializer.Serialize(safeConfig)
            });

            _logger.LogDebug("Stored provider configuration: {ProviderId} ({Name})", config.ProviderId, friendlyName ?? config.ProviderId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StoreHistoryAsync(IEnumerable<ProviderUsage> usages)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string providerUpsertSql = @"
                INSERT INTO providers (
                    provider_id, provider_name, plan_type, auth_source,
                    account_name, updated_at, is_active, config_json
                ) VALUES (
                    @ProviderId, @ProviderName, @PlanType, @AuthSource,
                    @AccountName, CURRENT_TIMESTAMP, @IsActive, '{}'
                )
                ON CONFLICT(provider_id) DO UPDATE SET
                    provider_name = CASE 
                        WHEN excluded.provider_name IS NOT NULL AND excluded.provider_name != '' THEN excluded.provider_name
                        ELSE providers.provider_name
                    END,
                    plan_type = CASE
                        WHEN excluded.plan_type IS NOT NULL AND excluded.plan_type != '' THEN excluded.plan_type
                        ELSE providers.plan_type
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
                    details_json
                ) VALUES (
                    @ProviderId,
                    @RequestsUsed, @RequestsAvailable, @RequestsPercentage,
                    @IsAvailable, @StatusMessage, @NextResetTime, @FetchedAt,
                    @DetailsJson
                )";

            var providerUpsertParameters = usages.Select(u => new
            {
                ProviderId = u.ProviderId,
                ProviderName = u.ProviderName,
                PlanType = u.PlanType.ToString().ToLowerInvariant(),
                AuthSource = u.AuthSource,
                AccountName = u.AccountName,
                IsActive = u.IsAvailable ? 1 : 0
            });

            await connection.ExecuteAsync(providerUpsertSql, providerUpsertParameters);

            var parameters = usages.Select(u => new
            {
                ProviderId = u.ProviderId,
                RequestsUsed = u.RequestsUsed,
                RequestsAvailable = u.RequestsAvailable,
                RequestsPercentage = u.RequestsPercentage,
                IsAvailable = u.IsAvailable ? 1 : 0,
                StatusMessage = u.Description ?? "",
                NextResetTime = u.NextResetTime?.ToString("O"),
                FetchedAt = (u.FetchedAt == default ? DateTime.UtcNow : u.FetchedAt).ToString("O"),
                DetailsJson = u.Details != null && u.Details.Any() 
                    ? JsonSerializer.Serialize(u.Details) 
                    : null
            });

            await connection.ExecuteAsync(sql, parameters);
            _logger.LogInformation("{Count} records stored", usages.Count());
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StoreRawSnapshotAsync(string providerId, string rawJson, int httpStatus)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO raw_snapshots (provider_id, raw_json, http_status, fetched_at)
                VALUES (@ProviderId, @RawJson, @HttpStatus, CURRENT_TIMESTAMP)";

            await connection.ExecuteAsync(sql, new { ProviderId = providerId, RawJson = rawJson, HttpStatus = httpStatus });
            _logger.LogDebug("Stored raw snapshot for {ProviderId}", providerId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task CleanupOldSnapshotsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = "DELETE FROM raw_snapshots WHERE fetched_at < datetime('now', '-14 days')";
            var deletedCount = await connection.ExecuteAsync(sql);
            
            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned {Count} snapshots", deletedCount);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task CleanupEmptyHistoryAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Delete history items that have 0 usage across the board and are unavailable.
            // This cleans up junk data generated by previously unconfigured providers.
            const string sql = @"
                DELETE FROM provider_history 
                WHERE requests_available = 0 
                  AND requests_used = 0 
                  AND requests_percentage = 0 
                  AND is_available = 0";
                  
            var deletedCount = await connection.ExecuteAsync(sql);
            
            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} empty provider history entries", deletedCount);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task OptimizeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("PRAGMA optimize");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StoreResetEventAsync(string providerId, string providerName, 
        double? previousUsage, double? newUsage, string resetType)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

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
                ResetType = resetType
            });

            _logger.LogInformation("Reset: {ProviderId} ({ResetType})", providerId, resetType);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetLatestHistoryAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT h.provider_id AS ProviderId,
                       COALESCE(NULLIF(p.provider_name, ''), h.provider_id) AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                       h.status_message AS Description, h.fetched_at AS FetchedAt,
                       h.next_reset_time AS NextResetTime, h.details_json AS DetailsJson,
                       COALESCE(p.account_name, '') AS AccountName,
                       COALESCE(p.auth_source, '') AS AuthSource,
                       CASE WHEN LOWER(COALESCE(p.plan_type, '')) = 'coding' THEN 1 ELSE 0 END AS PlanType
                FROM provider_history h
                LEFT JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.id IN (
                    SELECT MAX(id) FROM provider_history GROUP BY provider_id
                )
                ORDER BY h.provider_id";

            var results = (await connection.QueryAsync<ProviderUsage>(sql)).ToList();

            foreach (var usage in results.Where(u => !string.IsNullOrWhiteSpace(u.DetailsJson)))
            {
                try
                {
                    usage.Details = JsonSerializer.Deserialize<List<ProviderUsageDetail>>(usage.DetailsJson!);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse details_json for provider {ProviderId}", usage.ProviderId);
                }
            }

            foreach (var usage in results)
            {
                if (ProviderPlanClassifier.IsCodingPlanProvider(usage.ProviderId))
                {
                    usage.PlanType = PlanType.Coding;
                    usage.IsQuotaBased = true;
                }
                else if (usage.PlanType == PlanType.Coding)
                {
                    usage.IsQuotaBased = true;
                }

                if (!usage.DisplayAsFraction && usage.PlanType == PlanType.Coding && usage.RequestsAvailable > 100)
                {
                    usage.DisplayAsFraction = true;
                }
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = $@"
                SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                       h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                       h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                       h.status_message AS Description, h.fetched_at AS FetchedAt,
                       h.next_reset_time AS NextResetTime
                FROM provider_history h
                JOIN providers p ON h.provider_id = p.provider_id
                WHERE h.provider_id != 'antigravity'
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

    public async Task<List<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
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
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetRecentHistoryAsync(int countPerProvider)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = $@"
                WITH RankedHistory AS (
                    SELECT h.provider_id AS ProviderId, p.provider_name AS ProviderName,
                           h.requests_used AS RequestsUsed, h.requests_available AS RequestsAvailable,
                           h.requests_percentage AS RequestsPercentage, h.is_available AS IsAvailable,
                           h.status_message AS Description, h.fetched_at AS FetchedAt,
                           h.next_reset_time AS NextResetTime,
                           ROW_NUMBER() OVER (PARTITION BY h.provider_id ORDER BY h.fetched_at DESC) as pos
                    FROM provider_history h
                    JOIN providers p ON h.provider_id = p.provider_id
                )
                SELECT ProviderId, ProviderName, RequestsUsed, RequestsAvailable, 
                       RequestsPercentage, IsAvailable, Description, FetchedAt, NextResetTime
                FROM RankedHistory 
                WHERE pos <= @Count
                ORDER BY ProviderId, FetchedAt DESC";

            var results = await connection.QueryAsync<ProviderUsage>(sql, new { Count = countPerProvider });
            return results.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
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

    public async Task<bool> IsHistoryEmptyAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM provider_history");
            return count == 0;
        }
        finally
        {
            _semaphore.Release();
        }
    }
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
