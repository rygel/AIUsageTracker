// <copyright file="WebDatabaseServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public sealed class WebDatabaseServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly WebDatabaseService _service;
    private readonly IMemoryCache _cache;

    public WebDatabaseServiceTests()
    {
        this._tempDir = TestTempPaths.CreateDirectory("WebDatabaseServiceTests");
        this._dbPath = Path.Combine(this._tempDir, "monitor.db");
        this._cache = new MemoryCache(new MemoryCacheOptions());
        var pathProvider = new TestPathProvider(this._tempDir);

        this._service = new WebDatabaseService(
            this._cache,
            Mock.Of<ILogger<WebDatabaseService>>(),
            pathProvider);
    }

    public void Dispose()
    {
        this._cache.Dispose();
        TestTempPaths.CleanupPath(this._tempDir);
    }

    [Fact]
    public void IsDatabaseAvailable_ReturnsFalse_WhenNoDatabase()
    {
        Assert.False(this._service.IsDatabaseAvailable());
    }

    [Fact]
    public void IsDatabaseAvailable_ReturnsTrue_WhenDatabaseExists()
    {
        File.WriteAllText(this._dbPath, "test");
        Assert.True(this._service.IsDatabaseAvailable());
    }

    [Fact]
    public async Task GetProvidersAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetProvidersAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLatestUsageAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetLatestUsageAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetHistoryAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProviderHistoryAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetProviderHistoryAsync("openai");
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_ReturnsDefault_WhenNoDatabase()
    {
        var result = await this._service.GetUsageSummaryAsync();
        Assert.Equal(0, result.ProviderCount);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_CachesResult()
    {
        var first = await this._service.GetUsageSummaryAsync();
        var second = await this._service.GetUsageSummaryAsync();

        Assert.Equal(first.ProviderCount, second.ProviderCount);
    }

    [Fact]
    public async Task GetChartDataAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetChartDataAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentResetEventsAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetRecentResetEventsAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetResetEventsAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetResetEventsAsync("openai");
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetHistorySamplesAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetHistorySamplesAsync(new[] { "openai" }, 24, 100);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllHistoryForExportAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var result = await this._service.GetAllHistoryForExportAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProvidersRawAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var (rows, count) = await this._service.GetProvidersRawAsync();
        Assert.Empty(rows);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetProviderHistoryRawAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var (rows, count) = await this._service.GetProviderHistoryRawAsync();
        Assert.Empty(rows);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetRawSnapshotsRawAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var (rows, count) = await this._service.GetRawSnapshotsRawAsync();
        Assert.Empty(rows);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetResetEventsRawAsync_ReturnsEmpty_WhenNoDatabase()
    {
        var (rows, count) = await this._service.GetResetEventsRawAsync();
        Assert.Empty(rows);
        Assert.Equal(0, count);
    }

    [Fact]
    public void GetDatabasePath_ReturnsConfiguredPath()
    {
        Assert.Equal(this._dbPath, this._service.GetDatabasePath());
    }

    [Fact]
    public async Task GetProvidersAsync_WithDatabase_ReturnsProviders()
    {
        this.CreateTestDatabase();

        var result = await this._service.GetProvidersAsync();

        Assert.NotEmpty(result);
        Assert.Contains(result, p => string.Equals(p.ProviderId, "test-provider", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetUsageSummaryAsync_WithDatabase_ReturnsSummary()
    {
        this.CreateTestDatabase();

        var result = await this._service.GetUsageSummaryAsync();

        Assert.True(result.ProviderCount > 0);
    }

    [Fact]
    public async Task GetChartDataAsync_WithDatabase_ReturnsData()
    {
        this.CreateTestDatabase();

        var result = await this._service.GetChartDataAsync(hours: 24);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetRecentResetEventsAsync_WithDatabase_ReturnsEvents()
    {
        this.CreateTestDatabase();

        var result = await this._service.GetRecentResetEventsAsync(hours: 24);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetLatestUsageAsync_WithDatabase_ReturnsUsage()
    {
        this.CreateTestDatabase();

        var result = await this._service.GetLatestUsageAsync();

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetProvidersRawAsync_WithDatabase_ReturnsRows()
    {
        this.CreateTestDatabase();

        var (rows, count) = await this._service.GetProvidersRawAsync();

        Assert.True(count > 0);
        Assert.NotEmpty(rows);
    }

    private void CreateTestDatabase()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={this._dbPath}");
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS providers (
                    provider_id TEXT PRIMARY KEY,
                    provider_name TEXT NOT NULL,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    auth_source TEXT DEFAULT '',
                    account_name TEXT DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS provider_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider_id TEXT NOT NULL,
                    requests_percentage REAL DEFAULT 0,
                    requests_used REAL DEFAULT 0,
                    requests_available REAL DEFAULT 0,
                    is_available INTEGER DEFAULT 1,
                    status_message TEXT DEFAULT '',
                    response_latency_ms REAL DEFAULT 0,
                    next_reset_time TEXT,
                    fetched_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
                CREATE TABLE IF NOT EXISTS raw_snapshots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider_id TEXT NOT NULL,
                    raw_json TEXT DEFAULT '',
                    fetched_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
                CREATE TABLE IF NOT EXISTS reset_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider_id TEXT NOT NULL,
                    provider_name TEXT DEFAULT '',
                    previous_usage REAL DEFAULT 0,
                    new_usage REAL DEFAULT 0,
                    reset_type TEXT DEFAULT '',
                    timestamp TEXT NOT NULL DEFAULT (datetime('now'))
                );

                INSERT OR REPLACE INTO providers (provider_id, provider_name, is_active)
                VALUES ('test-provider', 'Test Provider', 1);

                INSERT INTO provider_history (provider_id, requests_percentage, requests_used, requests_available, fetched_at)
                VALUES ('test-provider', 50.0, 50.0, 100.0, datetime('now'));

                INSERT INTO reset_events (provider_id, provider_name, previous_usage, new_usage, timestamp)
                VALUES ('test-provider', 'Test Provider', 80.0, 10.0, datetime('now'));
            ";
            cmd.ExecuteNonQuery();
        }
    }

    private sealed class TestPathProvider : IAppPathProvider
    {
        private readonly string _root;

        public TestPathProvider(string root) => this._root = root;

        public string GetAppDataRoot() => this._root;

        public string GetDatabasePath() => Path.Combine(this._root, "monitor.db"); 
public string GetLogDirectory() => Path.Combine(this._root, "logs");

        public string GetAuthFilePath() => Path.Combine(this._root, "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this._root, "prefs.json");

        public string GetProviderConfigFilePath() => Path.Combine(this._root, "providers.json");

        public string GetMonitorInfoFilePath() => Path.Combine(this._root, "monitor.json");

        public string GetUserProfileRoot() => Path.Combine(this._root, "userprofile");
    }
}
