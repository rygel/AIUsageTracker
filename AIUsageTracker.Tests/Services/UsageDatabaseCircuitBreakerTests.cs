// <copyright file="UsageDatabaseCircuitBreakerTests.cs" company="AIUsageTracker">
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
/// Integration tests verifying that the write-dedup gate in <see cref="UsageDatabase"/>
/// correctly suppresses redundant history rows under circuit-breaker–like conditions —
/// where a provider stays in a fixed error/unavailable state over multiple consecutive polls.
///
/// The circuit breaker in the HTTP client layer (Polly) causes a provider to return the
/// same "service unavailable" response across many polls. These tests confirm that this
/// stable error state does not flood the database with identical rows.
/// </summary>
public sealed class UsageDatabaseCircuitBreakerTests : IDisposable
{
    private readonly string _dbPath;

    public UsageDatabaseCircuitBreakerTests()
    {
        this._dbPath = TestTempPaths.CreateFilePath("usage-db-circuit-breaker-tests", "usage.db");
    }

    // -------------------------------------------------------------------------
    // State transition: available → unavailable (circuit opens)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreHistoryAsync_AvailableToUnavailable_InsertsNewRowAsync()
    {
        var db = await CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-10);

        // First poll: provider is healthy
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: true, httpStatus: 200, statusMessage: "ok", fetchedAt: t1)]);

        // Second poll: circuit opens, provider now returns 503
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Service unavailable", fetchedAt: t1.AddMinutes(5))]);

        Assert.Equal(2, CountRows("codex")); // state changed → new row
    }

    // -------------------------------------------------------------------------
    // Circuit breaker open: repeated identical error state is deduplicated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreHistoryAsync_RepeatedSameErrorState_DoesNotFloodDatabaseAsync()
    {
        var db = await CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-10);

        // Initial availability → unavailable transition (circuit opens)
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: true, httpStatus: 200, statusMessage: "ok", fetchedAt: t1)]);
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Service unavailable", fetchedAt: t1.AddMinutes(1))]);

        // Simulate circuit breaker staying open across 5 more polls — same 503 each time
        for (var i = 2; i <= 6; i++)
        {
            await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Service unavailable", fetchedAt: t1.AddMinutes(i))]);
        }

        // Should be exactly 2 rows: the initial healthy row + one 503 row (all duplicates suppressed)
        Assert.Equal(2, CountRows("codex"));
    }

    [Fact]
    public async Task StoreHistoryAsync_RepeatedSameErrorState_UpdatesFetchedAtToLatestPollAsync()
    {
        var db = await CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-10);
        var t2 = t1.AddMinutes(1);
        var t3 = t1.AddMinutes(2);

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Circuit open", fetchedAt: t1)]);
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Circuit open", fetchedAt: t2)]);
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Circuit open", fetchedAt: t3)]);

        // Row count stays at 1 (all deduplicated)
        Assert.Equal(1, CountRows("codex"));

        // But fetched_at should be t3 (the most recent poll time)
        var storedFetchedAt = GetFetchedAt("codex");
        Assert.Equal(t3.ToString("O"), storedFetchedAt);
    }

    // -------------------------------------------------------------------------
    // Circuit breaker recovery: unavailable → available (circuit closes)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreHistoryAsync_UnavailableToAvailable_InsertsNewRowAsync()
    {
        var db = await CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-10);

        // Circuit open
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Service unavailable", fetchedAt: t1)]);

        // Circuit closes — provider recovers
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: true, httpStatus: 200, statusMessage: "ok", fetchedAt: t1.AddMinutes(5))]);

        Assert.Equal(2, CountRows("codex")); // recovery → new row
    }

    // -------------------------------------------------------------------------
    // Circuit breaker with different HTTP status transitions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreHistoryAsync_HttpStatusChangesDuringCircuitOpen_InsertsNewRowAsync()
    {
        var db = await CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-10);

        // Initial circuit trip at 503
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Service unavailable", fetchedAt: t1)]);

        // HTTP status changes to 429 (rate-limited instead of down)
        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 429, statusMessage: "Service unavailable", fetchedAt: t1.AddMinutes(2))]);

        Assert.Equal(2, CountRows("codex")); // http_status changed → new row
    }

    // -------------------------------------------------------------------------
    // Multiple providers independently circuit-break
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreHistoryAsync_MultipleProviders_CircuitBreakersOperateIndependentlyAsync()
    {
        var db = await CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-10);

        // Both start healthy
        await db.StoreHistoryAsync([
            MakeUsage("codex", isAvailable: true, httpStatus: 200, statusMessage: "ok", fetchedAt: t1),
            MakeUsage("mistral", isAvailable: true, httpStatus: 200, statusMessage: "ok", fetchedAt: t1),
        ]);

        // Only codex trips its circuit breaker
        await db.StoreHistoryAsync([
            MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Service unavailable", fetchedAt: t1.AddMinutes(1)),
            MakeUsage("mistral", isAvailable: true, httpStatus: 200, statusMessage: "ok", fetchedAt: t1.AddMinutes(1)),
        ]);

        // Repeated polls: codex stays down, mistral stays up (both unchanged)
        for (var i = 2; i <= 4; i++)
        {
            await db.StoreHistoryAsync([
                MakeUsage("codex", isAvailable: false, httpStatus: 503, statusMessage: "Service unavailable", fetchedAt: t1.AddMinutes(i)),
                MakeUsage("mistral", isAvailable: true, httpStatus: 200, statusMessage: "ok", fetchedAt: t1.AddMinutes(i)),
            ]);
        }

        Assert.Equal(2, CountRows("codex"));   // healthy row + one circuit-open row
        Assert.Equal(1, CountRows("mistral")); // never changed; all deduplicated
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
        bool isAvailable = true,
        int httpStatus = 200,
        string statusMessage = "ok",
        double requestsUsed = 50,
        DateTime fetchedAt = default)
    {
        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = providerId,
            RequestsUsed = requestsUsed,
            RequestsAvailable = 950,
            UsedPercent = requestsUsed / 1000 * 100,
            IsAvailable = isAvailable,
            Description = statusMessage,
            HttpStatus = httpStatus,
            FetchedAt = fetchedAt == default ? DateTime.UtcNow : fetchedAt,
        };
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

    private string? GetFetchedAt(string providerId)
    {
        using var connection = new SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT fetched_at FROM provider_history WHERE provider_id = $id ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$id", providerId);
        return cmd.ExecuteScalar()?.ToString();
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
