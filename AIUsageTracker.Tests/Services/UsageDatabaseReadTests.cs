// <copyright file="UsageDatabaseReadTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

/// <summary>
/// Integration tests for the read-path methods of <see cref="UsageDatabase"/>:
/// <see cref="UsageDatabase.GetHistoryAsync"/>, <see cref="UsageDatabase.GetHistoryByProviderAsync"/>,
/// <see cref="UsageDatabase.GetRecentHistoryAsync"/>, and <see cref="UsageDatabase.GetLatestHistoryAsync"/>.
///
/// These tests run against a real SQLite file so that Dapper column-to-property type mapping
/// is exercised end-to-end — the same class of bug that caused the production Int64/Int32
/// crash in the dedup gate.
/// </summary>
public sealed class UsageDatabaseReadTests : IDisposable
{
    private readonly string _dbPath;

    public UsageDatabaseReadTests()
    {
        this._dbPath = TestTempPaths.CreateFilePath("usage-db-read-tests", "usage.db");
    }

    // -------------------------------------------------------------------------
    // GetLatestHistoryAsync — stale-data detection
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetLatestHistoryAsync_RecentRow_IsNotStaleAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var recentFetch = DateTime.UtcNow.AddMinutes(-10); // within 1-hour threshold

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: true, fetchedAt: recentFetch)]);

        var results = await db.GetLatestHistoryAsync();

        var codex = Assert.Single(results, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.False(codex.IsStale);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_OldRow_IsStaleAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var oldFetch = DateTime.UtcNow.AddHours(-2); // beyond 1-hour threshold

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: true, statusMessage: "ok", fetchedAt: oldFetch)]);

        var results = await db.GetLatestHistoryAsync();

        var codex = Assert.Single(results, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.True(codex.IsStale);
        Assert.Contains("last refreshed", codex.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_WithProviderIds_ReturnsOnlyRequestedProvidersAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var now = DateTime.UtcNow.AddMinutes(-1);

        await db.StoreHistoryAsync([
            MakeUsage("codex", fetchedAt: now),
            MakeUsage("mistral", fetchedAt: now),
            MakeUsage("antigravity", fetchedAt: now),
        ]);

        var results = await db.GetLatestHistoryAsync(new[] { "codex", "antigravity" });

        Assert.Contains(results, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(results, u => string.Equals(u.ProviderId, "antigravity", StringComparison.Ordinal));
        Assert.DoesNotContain(results, u => string.Equals(u.ProviderId, "mistral", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetLatestHistoryAsync_WithEmptyProviderIds_ReturnsEmptyAsync()
    {
        var db = await this.CreateDatabaseAsync();
        await db.StoreHistoryAsync([MakeUsage("codex", fetchedAt: DateTime.UtcNow.AddMinutes(-1))]);

        var results = await db.GetLatestHistoryAsync(Array.Empty<string>());

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_WithNullProviderIds_ReturnsAllProvidersAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var now = DateTime.UtcNow.AddMinutes(-1);

        await db.StoreHistoryAsync([
            MakeUsage("codex", fetchedAt: now),
            MakeUsage("mistral", fetchedAt: now),
        ]);

        var results = await db.GetLatestHistoryAsync(null);

        Assert.Contains(results, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(results, u => string.Equals(u.ProviderId, "mistral", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetLatestHistoryAsync_RowOlderThan24h_IsExcludedFromResultsAsync()
    {
        // Rows older than 24 h are excluded at the SQL level — the provider must not appear at all.
        var db = await this.CreateDatabaseAsync();
        var veryOldFetch = DateTime.UtcNow.AddHours(-25);

        await db.StoreHistoryAsync([MakeUsage("antigravity", isAvailable: true, fetchedAt: veryOldFetch)]);

        var results = await db.GetLatestHistoryAsync();

        Assert.DoesNotContain(results, u => string.Equals(u.ProviderId, "antigravity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetLatestHistoryAsync_UnavailableOldRow_IsNotMarkedStaleAsync()
    {
        // IsAvailable=false entries carry their own description; stale suffix would be redundant.
        var db = await this.CreateDatabaseAsync();
        var oldFetch = DateTime.UtcNow.AddHours(-3);

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, statusMessage: "Auth token missing", fetchedAt: oldFetch)]);

        var results = await db.GetLatestHistoryAsync();

        var codex = Assert.Single(results, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.False(codex.IsStale);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_ReturnsCorrectTypeMapping_ForBoolAndIntColumnsAsync()
    {
        // Verify that bool IsAvailable and int HttpStatus survive the SQLite round-trip
        // without a Dapper type-mapping exception.
        var db = await this.CreateDatabaseAsync();
        var fetchedAt = DateTime.UtcNow.AddMinutes(-1);

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: true, httpStatus: 429, fetchedAt: fetchedAt)]);

        var results = await db.GetLatestHistoryAsync();

        var codex = Assert.Single(results, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.True(codex.IsAvailable);
        Assert.Equal(429, codex.HttpStatus);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_FalseIsAvailable_RoundTripsCorrectlyAsync()
    {
        var db = await this.CreateDatabaseAsync();

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, fetchedAt: DateTime.UtcNow)]);

        var results = await db.GetLatestHistoryAsync();

        var codex = Assert.Single(results, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.False(codex.IsAvailable);
        Assert.Equal(503, codex.HttpStatus);
    }

    [Fact]
    public async Task ReadMethods_DoNotBlockOnWriteSemaphoreAsync()
    {
        var db = await this.CreateDatabaseAsync();
        await db.StoreHistoryAsync([MakeUsage("codex", fetchedAt: DateTime.UtcNow)]);

        var semaphoreField = typeof(UsageDatabase).GetField("_semaphore", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(semaphoreField);
        var writeSemaphore = Assert.IsType<SemaphoreSlim>(semaphoreField!.GetValue(db));

        await writeSemaphore.WaitAsync();
        try
        {
            var latestTask = db.GetLatestHistoryAsync();
            var historyTask = db.GetHistoryAsync(limit: 1);
            var emptyTask = db.IsHistoryEmptyAsync();
            var readTasks = Task.WhenAll(latestTask, historyTask, emptyTask);

            var completed = await Task.WhenAny(readTasks, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.Same(readTasks, completed);
            Assert.Single(await latestTask);
            Assert.Single(await historyTask);
            Assert.False(await emptyTask);
        }
        finally
        {
            writeSemaphore.Release();
        }
    }

    // -------------------------------------------------------------------------
    // GetHistoryAsync — Dapper type mapping + pagination
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetHistoryAsync_ReturnsAllRowsUpToLimitAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var baseTime = DateTime.UtcNow.AddMinutes(-30);

        for (var i = 0; i < 5; i++)
        {
            await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: i * 10, fetchedAt: baseTime.AddMinutes(i))]);
        }

        var results = await db.GetHistoryAsync(limit: 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsRowsOrderedByFetchedAtDescendingAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var baseTime = DateTime.UtcNow.AddMinutes(-30);

        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 10, fetchedAt: baseTime)]);
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 20, fetchedAt: baseTime.AddMinutes(5))]);
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 30, fetchedAt: baseTime.AddMinutes(10))]);

        var results = await db.GetHistoryAsync();

        Assert.Equal(30.0, results[0].RequestsUsed); // newest first
        Assert.Equal(20.0, results[1].RequestsUsed);
        Assert.Equal(10.0, results[2].RequestsUsed);
    }

    [Fact]
    public async Task GetHistoryAsync_CorrectlyMapsBoolAndIntColumnsAsync()
    {
        // This would have crashed in production if ProviderUsage used a positional record.
        var db = await this.CreateDatabaseAsync();

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 429, fetchedAt: DateTime.UtcNow.AddMinutes(-1))]);

        var results = await db.GetHistoryAsync();

        Assert.Single(results);
        Assert.False(results[0].IsAvailable);
        Assert.Equal(429, results[0].HttpStatus);
    }

    [Fact]
    public async Task GetHistoryAsync_MultipleProviders_ReturnsAllInterleaved_OrderedByFetchedAtAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-10);
        var t2 = t1.AddMinutes(3);

        await db.StoreHistoryAsync([
            MakeUsage("codex", requestsUsed: 10, fetchedAt: t1),
            MakeUsage("mistral", requestsUsed: 20, fetchedAt: t2),
        ]);

        var results = await db.GetHistoryAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("mistral", results[0].ProviderId); // most recent first
        Assert.Equal("codex", results[1].ProviderId);
    }

    // -------------------------------------------------------------------------
    // GetHistoryByProviderAsync — filtering
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetHistoryByProviderAsync_FiltersToRequestedProviderOnlyAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-10);

        await db.StoreHistoryAsync([
            MakeUsage("codex", requestsUsed: 10, fetchedAt: t1),
            MakeUsage("mistral", requestsUsed: 20, fetchedAt: t1.AddMinutes(1)),
        ]);

        var results = await db.GetHistoryByProviderAsync("codex");

        Assert.All(results, r => Assert.Equal("codex", r.ProviderId));
        Assert.Single(results);
    }

    [Fact]
    public async Task GetHistoryByProviderAsync_LimitIsRespectedAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var baseTime = DateTime.UtcNow.AddMinutes(-30);

        for (var i = 0; i < 5; i++)
        {
            await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: i * 10, fetchedAt: baseTime.AddMinutes(i))]);
        }

        var results = await db.GetHistoryByProviderAsync("codex", limit: 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetHistoryByProviderAsync_CorrectlyMapsBoolAndIntColumnsAsync()
    {
        var db = await this.CreateDatabaseAsync();

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: true, httpStatus: 200, fetchedAt: DateTime.UtcNow.AddMinutes(-1))]);

        var results = await db.GetHistoryByProviderAsync("codex");

        Assert.Single(results);
        Assert.True(results[0].IsAvailable);
        Assert.Equal(200, results[0].HttpStatus);
    }

    [Fact]
    public async Task GetHistoryByProviderAsync_UnknownProvider_ReturnsEmptyAsync()
    {
        var db = await this.CreateDatabaseAsync();

        await db.StoreHistoryAsync([MakeUsage("codex", fetchedAt: DateTime.UtcNow.AddMinutes(-1))]);

        var results = await db.GetHistoryByProviderAsync("unknown-provider");

        Assert.Empty(results);
    }

    // -------------------------------------------------------------------------
    // GetRecentHistoryAsync — per-provider N-row slice
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetRecentHistoryAsync_ReturnsAtMostNRowsPerProviderAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var baseTime = DateTime.UtcNow.AddMinutes(-30);

        // 5 rows for codex, 3 rows for mistral
        for (var i = 0; i < 5; i++)
        {
            await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: i * 10, fetchedAt: baseTime.AddMinutes(i))]);
        }

        for (var i = 0; i < 3; i++)
        {
            await db.StoreHistoryAsync([MakeUsage("mistral", requestsUsed: i * 5, fetchedAt: baseTime.AddMinutes(i))]);
        }

        var results = await db.GetRecentHistoryAsync(countPerProvider: 2);

        var codexRows = results.Where(r => string.Equals(r.ProviderId, "codex", StringComparison.Ordinal)).ToList();
        var mistralRows = results.Where(r => string.Equals(r.ProviderId, "mistral", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, codexRows.Count);
        Assert.Equal(2, mistralRows.Count);
    }

    [Fact]
    public async Task GetRecentHistoryAsync_CorrectlyMapsBoolAndIntColumnsAsync()
    {
        var db = await this.CreateDatabaseAsync();

        await db.StoreHistoryAsync([MakeUsage("codex", isAvailable: false, httpStatus: 503, fetchedAt: DateTime.UtcNow.AddMinutes(-1))]);

        var results = await db.GetRecentHistoryAsync(countPerProvider: 5);

        Assert.Single(results);
        Assert.False(results[0].IsAvailable);
        Assert.Equal(503, results[0].HttpStatus);
    }

    [Fact]
    public async Task GetRecentHistoryAsync_ReturnsNewestRowsPerProviderAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var baseTime = DateTime.UtcNow.AddMinutes(-30);

        // Insert 3 rows for codex; oldest has requestsUsed=5, newest has requestsUsed=25
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 5, fetchedAt: baseTime)]);
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 15, fetchedAt: baseTime.AddMinutes(5))]);
        await db.StoreHistoryAsync([MakeUsage("codex", requestsUsed: 25, fetchedAt: baseTime.AddMinutes(10))]);

        var results = await db.GetRecentHistoryAsync(countPerProvider: 2);

        var codexRows = results.Where(r => string.Equals(r.ProviderId, "codex", StringComparison.Ordinal)).OrderByDescending(r => r.FetchedAt).ToList();
        Assert.Equal(2, codexRows.Count);
        Assert.Equal(25.0, codexRows[0].RequestsUsed); // newest
        Assert.Equal(15.0, codexRows[1].RequestsUsed); // second newest
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
