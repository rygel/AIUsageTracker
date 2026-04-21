// <copyright file="UsageDatabaseDedupTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

/// <summary>
/// Integration tests for the provider_history write-dedup gate and periodic compaction
/// in <see cref="UsageDatabase"/>. These tests run against a real SQLite database so that
/// Dapper type-mapping bugs (e.g. Int32 vs Int64 for INTEGER columns) are caught at the
/// unit-test stage rather than in production.
/// </summary>
public sealed class UsageDatabaseDedupTests : IDisposable
{
    private readonly string _dbPath;

    public UsageDatabaseDedupTests()
    {
        this._dbPath = TestTempPaths.CreateFilePath("usage-db-dedup-tests", "usage.db");
    }

    // -------------------------------------------------------------------------
    // Dedup gate — no insert when data is unchanged
    // -------------------------------------------------------------------------
    [Fact]
    public async Task StoreHistoryAsync_IdenticalData_DoesNotInsertNewRowAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);

        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 50, fetchedAt: t1)]);
        Assert.Equal(1, this.CountRows("codex"));

        var t2 = t1.AddMinutes(5);
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 50, fetchedAt: t2)]);
        Assert.Equal(1, this.CountRows("codex"));
    }

    [Fact]
    public async Task StoreHistoryAsync_IdenticalData_UpdatesFetchedAtOnExistingRowAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = new DateTime(2026, 3, 19, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMinutes(5);

        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 50, fetchedAt: t1)]);
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 50, fetchedAt: t2)]);

        var storedFetchedAt = this.GetFetchedAt("codex");
        Assert.Equal(EpochFloor(t2), storedFetchedAt);
    }

    // -------------------------------------------------------------------------
    // Dedup gate — insert when any meaningful field changes
    // -------------------------------------------------------------------------
    [Fact]
    public async Task StoreHistoryAsync_ChangedRequestsUsed_InsertsNewRowAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);

        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 50, fetchedAt: t1)]);
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 75, fetchedAt: t1.AddMinutes(5))]);

        Assert.Equal(2, this.CountRows("codex"));
    }

    [Fact]
    public async Task StoreHistoryAsync_ChangedIsAvailable_InsertsNewRowAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: true, fetchedAt: t1)]);
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, fetchedAt: t1.AddMinutes(5))]);

        Assert.Equal(2, this.CountRows("codex"));
    }

    [Fact]
    public async Task StoreHistoryAsync_ChangedStatusMessage_InsertsNewRowAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);

        await db.StoreHistoryAsync([MakeUsage("codex", statusMessage: "ok", fetchedAt: t1)]);
        await db.StoreHistoryAsync([MakeUsage("codex", statusMessage: "degraded", fetchedAt: t1.AddMinutes(5))]);

        Assert.Equal(2, this.CountRows("codex"));
    }

    [Fact]
    public async Task StoreHistoryAsync_ChangedHttpStatus_InsertsNewRowAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);

        await db.StoreHistoryAsync([MakeUsage("codex", httpStatus: 200, fetchedAt: t1)]);
        await db.StoreHistoryAsync([MakeUsage("codex", httpStatus: 429, fetchedAt: t1.AddMinutes(5))]);

        Assert.Equal(2, this.CountRows("codex"));
    }

    [Fact]
    public async Task StoreHistoryAsync_ChangedFlatCardName_InsertsNewRowAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);

        await db.StoreHistoryAsync([
            new ProviderUsage
            {
                ProviderId = "openrouter",
                ProviderName = "OpenRouter",
                CardId = "credits",
                Name = "Credits",
                GroupId = "openrouter",
                RequestsUsed = 2.5,
                RequestsAvailable = 10,
                UsedPercent = 25,
                IsAvailable = true,
                Description = "7.50 Credits Remaining",
                HttpStatus = 200,
                FetchedAt = t1,
            },
        ]);

        await db.StoreHistoryAsync([
            new ProviderUsage
            {
                ProviderId = "openrouter",
                ProviderName = "OpenRouter",
                CardId = "credits",
                Name = "Openrouter Credits",
                GroupId = "openrouter",
                RequestsUsed = 2.5,
                RequestsAvailable = 10,
                UsedPercent = 25,
                IsAvailable = true,
                Description = "7.50 Credits Remaining",
                HttpStatus = 200,
                FetchedAt = t1.AddMinutes(5),
            },
        ]);

        Assert.Equal(2, this.CountRows("openrouter"));
    }

    [Fact]
    public async Task StoreHistoryAsync_ChangedUsedPercent_InsertsNewRowAsync()
    {
        // Previously tested Details JSON changes; now tests flat card UsedPercent changes
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);

        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 50, fetchedAt: t1)]);
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 75, fetchedAt: t1.AddMinutes(5))]);

        Assert.Equal(2, this.CountRows("codex"));
    }

    [Fact]
    public async Task StoreHistoryAsync_FirstInsertForProvider_InsertsRowAsync()
    {
        var db = await this.CreateDatabaseAsync();

        await db.StoreHistoryAsync([MakeUsage("new-provider", fetchedAt: DateTime.UtcNow)]);

        Assert.Equal(1, this.CountRows("new-provider"));
    }

    // -------------------------------------------------------------------------
    // Dedup gate — multiple providers in one batch
    // -------------------------------------------------------------------------
    [Fact]
    public async Task StoreHistoryAsync_MixedBatch_InsertsChangedAndTouchesUnchangedAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);

        await db.StoreHistoryAsync([
            MakeUsage("codex", requestsUsed: 50, fetchedAt: t1),
            MakeUsage("mistral", requestsUsed: 20, fetchedAt: t1),
        ]);

        var t2 = t1.AddMinutes(5);
        await db.StoreHistoryAsync([
            MakeUsage("codex", requestsUsed: 50, fetchedAt: t2),   // unchanged
            MakeUsage("mistral", requestsUsed: 30, fetchedAt: t2), // changed
        ]);

        Assert.Equal(1, this.CountRows("codex"));    // deduped
        Assert.Equal(2, this.CountRows("mistral"));  // new row

        Assert.Equal(EpochFloor(t2), this.GetFetchedAt("codex"));
    }

    // -------------------------------------------------------------------------
    // Compaction
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CompactHistoryAsync_RowsWithinSevenDays_AreUntouchedAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var now = DateTime.UtcNow;

        // Insert 5 rows at 5-minute intervals within the last 7 days
        for (var i = 0; i < 5; i++)
        {
            this.InsertHistoryRow("codex", requestsUsed: i, fetchedAt: now.AddMinutes(-i * 5));
        }

        await db.CompactHistoryAsync();

        Assert.Equal(5, this.CountRows("codex"));
    }

    [Fact]
    public async Task CompactHistoryAsync_OldRowsBeyondSevenDays_DownsamplesToOnePerHourAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var raw = DateTime.UtcNow.AddDays(-10);
        var baseTime = new DateTime(raw.Year, raw.Month, raw.Day, raw.Hour, 0, 0, DateTimeKind.Utc); // clearly in 7–90d window; truncate to hour start so rows don't span buckets

        // Insert 3 rows in the same hour (should keep only 1 — the last)
        this.InsertHistoryRow("codex", requestsUsed: 1, fetchedAt: baseTime);
        this.InsertHistoryRow("codex", requestsUsed: 2, fetchedAt: baseTime.AddMinutes(15));
        this.InsertHistoryRow("codex", requestsUsed: 3, fetchedAt: baseTime.AddMinutes(30));

        // Insert 2 rows in a different hour (should also keep only 1)
        this.InsertHistoryRow("codex", requestsUsed: 4, fetchedAt: baseTime.AddHours(1));
        this.InsertHistoryRow("codex", requestsUsed: 5, fetchedAt: baseTime.AddHours(1).AddMinutes(20));

        Assert.Equal(5, this.CountRows("codex"));

        await db.CompactHistoryAsync();

        Assert.Equal(2, this.CountRows("codex")); // 1 per hour
    }

    [Fact]
    public async Task CompactHistoryAsync_KeepsLastRowOfEachHourBucketAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var raw = DateTime.UtcNow.AddDays(-10);
        var baseTime = new DateTime(raw.Year, raw.Month, raw.Day, raw.Hour, 0, 0, DateTimeKind.Utc); // truncate to hour start

        this.InsertHistoryRow("codex", requestsUsed: 10, fetchedAt: baseTime);
        this.InsertHistoryRow("codex", requestsUsed: 20, fetchedAt: baseTime.AddMinutes(20));
        this.InsertHistoryRow("codex", requestsUsed: 30, fetchedAt: baseTime.AddMinutes(40)); // last in hour — kept

        await db.CompactHistoryAsync();

        Assert.Equal(1, this.CountRows("codex"));
        Assert.Equal(30.0, this.GetRequestsUsed("codex"));
    }

    [Fact]
    public async Task CompactHistoryAsync_RowsOlderThan90Days_DownsamplesToOnePerDayAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var raw = DateTime.UtcNow.AddDays(-95);
        var baseTime = new DateTime(raw.Year, raw.Month, raw.Day, 0, 0, 0, DateTimeKind.Utc); // clearly >90d; truncate to day start so rows don't span day buckets

        // 3 rows on the same day → keep 1
        this.InsertHistoryRow("codex", requestsUsed: 1, fetchedAt: baseTime);
        this.InsertHistoryRow("codex", requestsUsed: 2, fetchedAt: baseTime.AddHours(6));
        this.InsertHistoryRow("codex", requestsUsed: 3, fetchedAt: baseTime.AddHours(12));

        // 2 rows on a different day → keep 1
        this.InsertHistoryRow("codex", requestsUsed: 4, fetchedAt: baseTime.AddDays(1));
        this.InsertHistoryRow("codex", requestsUsed: 5, fetchedAt: baseTime.AddDays(1).AddHours(6));

        Assert.Equal(5, this.CountRows("codex"));

        await db.CompactHistoryAsync();

        Assert.Equal(2, this.CountRows("codex")); // 1 per day
    }

    [Fact]
    public async Task CompactHistoryAsync_DoesNotAffectOtherProvidersAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var raw = DateTime.UtcNow.AddDays(-10);
        var baseTime = new DateTime(raw.Year, raw.Month, raw.Day, raw.Hour, 0, 0, DateTimeKind.Utc); // truncate to hour start

        // 3 old rows for codex (should be compacted to 1)
        this.InsertHistoryRow("codex", requestsUsed: 1, fetchedAt: baseTime);
        this.InsertHistoryRow("codex", requestsUsed: 2, fetchedAt: baseTime.AddMinutes(10));
        this.InsertHistoryRow("codex", requestsUsed: 3, fetchedAt: baseTime.AddMinutes(20));

        // 3 recent rows for mistral (should be untouched)
        var recent = DateTime.UtcNow.AddMinutes(-15);
        this.InsertHistoryRow("mistral", requestsUsed: 10, fetchedAt: recent);
        this.InsertHistoryRow("mistral", requestsUsed: 11, fetchedAt: recent.AddMinutes(5));
        this.InsertHistoryRow("mistral", requestsUsed: 12, fetchedAt: recent.AddMinutes(10));

        await db.CompactHistoryAsync();

        Assert.Equal(1, this.CountRows("codex"));
        Assert.Equal(3, this.CountRows("mistral"));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    public void Dispose() => TestTempPaths.CleanupPath(this._dbPath);

    private async Task<UsageDatabase> CreateDatabaseAsync()
    {
        var db = new UsageDatabase(NullLogger<UsageDatabase>.Instance, new TestDbPathProvider(this._dbPath));
        await db.InitializeAsync().ConfigureAwait(false);
        return db;
    }

    private static ProviderUsage MakeUsage(
        string providerId,
        double requestsUsed = 50,
        double requestsAvailable = 950,
        bool isAvailable = true,
        string statusMessage = "ok",
        int httpStatus = 200,
        DateTime fetchedAt = default)
    {
        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = providerId,
            RequestsUsed = requestsUsed,
            RequestsAvailable = requestsAvailable,
            UsedPercent = requestsAvailable > 0 ? requestsUsed / requestsAvailable * 100 : 0,
            IsAvailable = isAvailable,
            Description = statusMessage,
            HttpStatus = httpStatus,
            FetchedAt = fetchedAt == default ? DateTime.UtcNow : fetchedAt,
        };
    }

    private void InsertHistoryRow(string providerId, double requestsUsed, DateTime fetchedAt)
    {
        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();

        using var ensureProvider = connection.CreateCommand();
        ensureProvider.CommandText =
            "INSERT OR IGNORE INTO providers (provider_id, provider_name, is_active) VALUES ($id, $id, 1)";
        ensureProvider.Parameters.AddWithValue("$id", providerId);
        ensureProvider.ExecuteNonQuery();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO provider_history
                (provider_id, requests_used, requests_available, requests_percentage,
                 is_available, status_message, fetched_at)
            VALUES ($id, $used, 1000, $pct, 1, '', $at)";
        cmd.Parameters.AddWithValue("$id", providerId);
        cmd.Parameters.AddWithValue("$used", requestsUsed);
        cmd.Parameters.AddWithValue("$pct", requestsUsed / 1000.0 * 100.0);
        cmd.Parameters.AddWithValue("$at", new DateTimeOffset(fetchedAt.Kind == DateTimeKind.Utc ? fetchedAt : fetchedAt.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    private int CountRows(string providerId)
    {
        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM provider_history WHERE provider_id = $id";
        cmd.Parameters.AddWithValue("$id", providerId);
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private DateTime? GetFetchedAt(string providerId)
    {
        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT fetched_at FROM provider_history WHERE provider_id = $id ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$id", providerId);
        var raw = cmd.ExecuteScalar();
        if (raw is null || raw == DBNull.Value)
        {
            return null;
        }

        var epoch = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
        return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
    }

    // Epoch storage has second-level precision; floor expected DateTimes to match.
    private static DateTime EpochFloor(DateTime dt) =>
        DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds()).UtcDateTime;

    private double GetRequestsUsed(string providerId)
    {
        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT requests_used FROM provider_history WHERE provider_id = $id ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$id", providerId);
        return Convert.ToDouble(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class TestDbPathProvider(string dbPath) : IAppPathProvider
    {
        public string GetAppDataRoot() => Path.GetDirectoryName(dbPath)!;

        public string GetDatabasePath() => dbPath;

        public string GetLogDirectory() => Path.Combine(this.GetAppDataRoot(), "logs");

        public string GetAuthFilePath() => Path.Combine(this.GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this.GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this.GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => this.GetAppDataRoot();

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }
}
