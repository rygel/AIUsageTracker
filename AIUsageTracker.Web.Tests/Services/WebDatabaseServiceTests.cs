// <copyright file="WebDatabaseServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Tests.Infrastructure;
using AIUsageTracker.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AIUsageTracker.Web.Tests.Services;

[TestClass]
public class WebDatabaseServiceTests
{
    private string _tempDirectory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        this._tempDirectory = TestTempPaths.CreateDirectory(
            "WebDatabaseServiceTests-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
    }

    [TestCleanup]
    public void Cleanup()
    {
        TestTempPaths.CleanupPath(this._tempDirectory);
    }

    [TestMethod]
    public async Task GetLatestUsageAsync_IncludeInactiveFalse_ReturnsOnlyAvailableActiveProvidersAsync()
    {
        var databasePath = this.CreateSeededDatabase();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = this.CreateService(databasePath, cache);

        var result = await service.GetLatestUsageAsync(includeInactive: false);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("openai", result[0].ProviderId);
        Assert.IsTrue(result[0].IsAvailable);
    }

    [TestMethod]
    public async Task GetLatestUsageAsync_IncludeInactiveTrue_ReturnsAllLatestRowsAsync()
    {
        var databasePath = this.CreateSeededDatabase();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = this.CreateService(databasePath, cache);

        var result = await service.GetLatestUsageAsync(includeInactive: true);

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public async Task GetUsageSummaryAsync_UsesCache_WhenDatabaseBecomesUnavailableAsync()
    {
        var databasePath = this.CreateSeededDatabase();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = this.CreateService(databasePath, cache);

        var summary = await service.GetUsageSummaryAsync();
        Assert.AreEqual(2, summary.ProviderCount);
        Assert.AreEqual(65.0, summary.AverageUsage, 0.01);

        var unavailableService = this.CreateService(
            Path.Combine(this._tempDirectory, "missing-after-cache.db"),
            cache);

        var cachedSummary = await unavailableService.GetUsageSummaryAsync();
        Assert.AreEqual(summary.ProviderCount, cachedSummary.ProviderCount);
        Assert.AreEqual(summary.AverageUsage, cachedSummary.AverageUsage, 0.01);
    }

    [TestMethod]
    public async Task GetProvidersAsync_WhenDatabaseMissing_ReturnsEmptyListAsync()
    {
        var databasePath = Path.Combine(this._tempDirectory, "missing.db");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = this.CreateService(databasePath, cache);

        var providers = await service.GetProvidersAsync();

        Assert.AreEqual(0, providers.Count);
    }

    private WebDatabaseService CreateService(string databasePath, IMemoryCache cache)
    {
        return new WebDatabaseService(
            cache,
            NullLogger<WebDatabaseService>.Instance,
            new TestAppPathProvider(databasePath));
    }

    private string CreateSeededDatabase()
    {
        var databasePath = Path.Combine(this._tempDirectory, "usage.db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE providers (
    provider_id TEXT PRIMARY KEY,
    provider_name TEXT NOT NULL,
    is_active INTEGER NOT NULL,
    auth_source TEXT NULL,
    account_name TEXT NULL
);

CREATE TABLE provider_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    requests_used REAL NOT NULL,
    requests_available REAL NOT NULL,
    requests_percentage REAL NOT NULL,
    is_available INTEGER NOT NULL,
    status_message TEXT NULL,
    response_latency_ms REAL NOT NULL,
    fetched_at TEXT NOT NULL,
    next_reset_time TEXT NULL
);";
        command.ExecuteNonQuery();

        this.SeedProviderRows(connection);
        this.SeedHistoryRows(connection);

        return databasePath;
    }

    private void SeedProviderRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO providers (provider_id, provider_name, is_active, auth_source, account_name)
VALUES ('openai', 'OpenAI Raw', 1, 'api_key', 'acct-openai');

INSERT INTO providers (provider_id, provider_name, is_active, auth_source, account_name)
VALUES ('claude', 'Claude Raw', 1, 'api_key', 'acct-claude');";
        command.ExecuteNonQuery();
    }

    private void SeedHistoryRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO provider_history (
    provider_id,
    requests_used,
    requests_available,
    requests_percentage,
    is_available,
    status_message,
    response_latency_ms,
    fetched_at,
    next_reset_time)
VALUES (
    'openai',
    10,
    90,
    10,
    1,
    'ok',
    120,
    '2026-03-10 10:00:00',
    '2026-03-11 00:00:00');

INSERT INTO provider_history (
    provider_id,
    requests_used,
    requests_available,
    requests_percentage,
    is_available,
    status_message,
    response_latency_ms,
    fetched_at,
    next_reset_time)
VALUES (
    'claude',
    60,
    40,
    120,
    0,
    'down',
    200,
    '2026-03-10 10:00:00',
    NULL);";
        command.ExecuteNonQuery();
    }

    private sealed class TestAppPathProvider : IAppPathProvider
    {
        private readonly string _databasePath;

        public TestAppPathProvider(string databasePath)
        {
            this._databasePath = databasePath;
        }

        public string GetAppDataRoot() => Path.GetDirectoryName(this._databasePath) ?? string.Empty;

        public string GetDatabasePath() => this._databasePath;

        public string GetLogDirectory() => this.GetAppDataRoot();

        public string GetAuthFilePath() => Path.Combine(this.GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this.GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this.GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => this.GetAppDataRoot();

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }
}
