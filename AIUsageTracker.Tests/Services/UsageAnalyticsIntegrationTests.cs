// <copyright file="UsageAnalyticsIntegrationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Services;

public class UsageAnalyticsIntegrationTests
{
    [Fact]
    public async Task GetBurnRateForecastsAsync_ComputesForecastFromDatabaseAsync()
    {
        var now = DateTime.UtcNow;
        var rows = new[]
        {
            CreateRow("openai", 10, 100, true, now.AddHours(-3).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)),
            CreateRow("openai", 20, 100, true, now.AddHours(-2).ToString("O")),
            CreateRow("openai", 30, 100, true, now.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)),
        };

        var dbPath = CreateTempDbPath();
        try
        {
            await SeedHistoryAsync(dbPath, rows);
            using var cache = new MemoryCache(new MemoryCacheOptions());

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(dbPath);

            var repo = new WebDatabaseService(cache, NullLogger<WebDatabaseService>.Instance, mockPathProvider.Object);
            var service = new UsageAnalyticsService(repo, cache, NullLogger<UsageAnalyticsService>.Instance);

            var forecasts = await service.GetBurnRateForecastsAsync(new[] { "openai" }, lookbackHours: 12, maxSamplesPerProvider: 100);

            Assert.True(forecasts.TryGetValue("openai", out var forecast));
            Assert.NotNull(forecast);
            Assert.True(forecast!.IsAvailable);
            Assert.Equal(3, forecast.SampleCount);
            Assert.True(forecast.BurnRatePerDay > 0);
        }
        finally
        {
            SafeDelete(dbPath);
        }
    }

    [Fact]
    public async Task GetUsageAnomaliesAsync_DetectsSpikeInDatabaseAsync()
    {
        var now = DateTime.UtcNow;
        var rows = new[]
        {
            CreateRow("openai", 10, 200, true, now.AddHours(-4).ToString("O")),
            CreateRow("openai", 20, 200, true, now.AddHours(-3).ToString("O")),
            CreateRow("openai", 30, 200, true, now.AddHours(-2).ToString("O")),
            CreateRow("openai", 120, 200, true, now.AddHours(-1).ToString("O")),
        };

        var dbPath = CreateTempDbPath();
        try
        {
            await SeedHistoryAsync(dbPath, rows);
            using var cache = new MemoryCache(new MemoryCacheOptions());

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(dbPath);

            var repo = new WebDatabaseService(cache, NullLogger<WebDatabaseService>.Instance, mockPathProvider.Object);
            var service = new UsageAnalyticsService(repo, cache, NullLogger<UsageAnalyticsService>.Instance);

            var anomalies = await service.GetUsageAnomaliesAsync(new[] { "openai" }, lookbackHours: 24, maxSamplesPerProvider: 100);

            Assert.True(anomalies.TryGetValue("openai", out var anomaly));
            Assert.NotNull(anomaly);
            Assert.True(anomaly!.IsAvailable);
            Assert.True(anomaly.HasAnomaly);
            Assert.Equal("Spike", anomaly.Direction);
        }
        finally
        {
            SafeDelete(dbPath);
        }
    }

    [Fact]
    public async Task GetBurnRateForecastsAsync_CalculatesSteadyExhaustionAsync()
    {
        var now = DateTime.UtcNow;

        // Usage: 10 units per hour
        // Available: 1000 total
        var rows = new List<HistoryRow>();
        for (int i = 5; i >= 0; i--)
        {
            rows.Add(CreateRow("steady-p", 500 + ((5 - i) * 10), 1000, true, now.AddHours(-i).ToString("O")));
        }

        var dbPath = CreateTempDbPath();
        try
        {
            await SeedHistoryAsync(dbPath, rows);
            using var cache = new MemoryCache(new MemoryCacheOptions());

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(dbPath);

            var repo = new WebDatabaseService(cache, NullLogger<WebDatabaseService>.Instance, mockPathProvider.Object);
            var service = new UsageAnalyticsService(repo, cache, NullLogger<UsageAnalyticsService>.Instance);

            var forecasts = await service.GetBurnRateForecastsAsync(new[] { "steady-p" });

            Assert.True(forecasts.TryGetValue("steady-p", out var forecast));
            Assert.True(forecast.IsAvailable);

            // Burn rate: 10/hour = 240/day
            Assert.Equal(240.0, forecast.BurnRatePerDay, 1);

            // Remaining: 1000 - 550 = 450
            // Days until exhausted: 450 / 240 = 1.875 days
            Assert.Equal(1.875, forecast.DaysUntilExhausted, 1);
        }
        finally
        {
            SafeDelete(dbPath);
        }
    }

    private static string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"ai-usage-tracker-tests-{Guid.NewGuid():N}.db");
    }

    private static async Task SeedHistoryAsync(string dbPath, IEnumerable<HistoryRow> rows)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            const string createSql = @"
            CREATE TABLE providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                is_active INTEGER DEFAULT 1
            );
            CREATE TABLE provider_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                requests_used REAL NOT NULL,
                requests_available REAL NOT NULL,
                requests_percentage REAL DEFAULT 0,
                is_available INTEGER NOT NULL,
                status_message TEXT,
                fetched_at TEXT NOT NULL,
                next_reset_time TEXT,
                response_latency_ms REAL NOT NULL DEFAULT 0
            );";
            await using (var createCommand = connection.CreateCommand())
            {
                createCommand.CommandText = createSql;
                await createCommand.ExecuteNonQueryAsync();
            }

            const string insertSql = @"
            INSERT INTO provider_history (
                provider_id, requests_used, requests_available, is_available, fetched_at, response_latency_ms
            ) VALUES (
                $providerId, $requestsUsed, $requestsAvailable, $isAvailable, $fetchedAt, $responseLatencyMs
            );";

            foreach (var row in rows)
            {
                await using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = insertSql;
                insertCommand.Parameters.AddWithValue("$providerId", row.ProviderId);
                insertCommand.Parameters.AddWithValue("$requestsUsed", row.RequestsUsed);
                insertCommand.Parameters.AddWithValue("$requestsAvailable", row.RequestsAvailable);
                insertCommand.Parameters.AddWithValue("$isAvailable", row.IsAvailable ? 1 : 0);
                insertCommand.Parameters.AddWithValue("$fetchedAt", row.FetchedAt);
                insertCommand.Parameters.AddWithValue("$responseLatencyMs", row.ResponseLatencyMs);
                await insertCommand.ExecuteNonQueryAsync();
            }
        }
    }

    private static HistoryRow CreateRow(
        string providerId,
        double requestsUsed,
        double requestsAvailable,
        bool isAvailable,
        string fetchedAt,
        double responseLatencyMs = 0)
    {
        return new HistoryRow(providerId, requestsUsed, requestsAvailable, isAvailable, fetchedAt, responseLatencyMs);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record HistoryRow(
        string ProviderId,
        double RequestsUsed,
        double RequestsAvailable,
        bool IsAvailable,
        string FetchedAt,
        double ResponseLatencyMs);
}
