using AIUsageTracker.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

public class WebDatabaseServiceBurnRateIntegrationTests
{
    [Fact]
    public async Task GetBurnRateForecastsAsync_WithMixedTimestampFormats_ComputesForecast()
    {
        var now = DateTime.UtcNow;
        var rows = new[]
        {
            CreateRow("openai", 10, 100, true, now.AddHours(-3).ToString("yyyy-MM-dd HH:mm:ss")),
            CreateRow("openai", 20, 100, true, now.AddHours(-2).ToString("O")),
            CreateRow("openai", 30, 100, true, now.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss"))
        };

        var dbPath = CreateTempDbPath();
        try
        {
            await SeedHistoryAsync(dbPath, rows);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new WebDatabaseService(cache, NullLogger<WebDatabaseService>.Instance, dbPath);

            var forecasts = await service.GetBurnRateForecastsAsync(new[] { "openai" }, lookbackHours: 12, maxSamplesPerProvider: 100);

            Assert.True(forecasts.TryGetValue("openai", out var forecast));
            Assert.NotNull(forecast);
            Assert.True(forecast!.IsAvailable);
            Assert.Equal(3, forecast.SampleCount);
            Assert.True(forecast.BurnRatePerDay > 0);
            Assert.True(forecast.DaysUntilExhausted > 0);
        }
        finally
        {
            SafeDelete(dbPath);
        }
    }

    [Fact]
    public async Task GetBurnRateForecastsAsync_AfterReset_UsesLatestCycleOnly()
    {
        var now = DateTime.UtcNow;
        var rows = new[]
        {
            CreateRow("anthropic", 70, 100, true, now.AddHours(-4).ToString("O")),
            CreateRow("anthropic", 80, 100, true, now.AddHours(-3).ToString("O")),
            CreateRow("anthropic", 5, 100, true, now.AddHours(-2).ToString("O")),
            CreateRow("anthropic", 15, 100, true, now.ToString("O"))
        };

        var dbPath = CreateTempDbPath();
        try
        {
            await SeedHistoryAsync(dbPath, rows);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new WebDatabaseService(cache, NullLogger<WebDatabaseService>.Instance, dbPath);

            var forecasts = await service.GetBurnRateForecastsAsync(new[] { "anthropic" }, lookbackHours: 12, maxSamplesPerProvider: 100);

            Assert.True(forecasts.TryGetValue("anthropic", out var forecast));
            Assert.NotNull(forecast);
            Assert.True(forecast!.IsAvailable);
            Assert.Equal(2, forecast.SampleCount);
            Assert.Equal(85, forecast.RemainingUnits, 3);
            Assert.Equal(120, forecast.BurnRatePerDay, 3);
        }
        finally
        {
            SafeDelete(dbPath);
        }
    }

    [Fact]
    public async Task GetUsageAnomaliesAsync_WithSuddenSpike_ReturnsDetectedAnomaly()
    {
        var now = DateTime.UtcNow;
        var rows = new[]
        {
            CreateRow("openai", 10, 200, true, now.AddHours(-4).ToString("O")),
            CreateRow("openai", 20, 200, true, now.AddHours(-3).ToString("O")),
            CreateRow("openai", 30, 200, true, now.AddHours(-2).ToString("O")),
            CreateRow("openai", 120, 200, true, now.AddHours(-1).ToString("O"))
        };

        var dbPath = CreateTempDbPath();
        try
        {
            await SeedHistoryAsync(dbPath, rows);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new WebDatabaseService(cache, NullLogger<WebDatabaseService>.Instance, dbPath);

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

    private static string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"ai-usage-tracker-tests-{Guid.NewGuid():N}.db");
    }

    private static async Task SeedHistoryAsync(string dbPath, IEnumerable<HistoryRow> rows)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        const string createSql = @"
            CREATE TABLE provider_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                requests_used REAL NOT NULL,
                requests_available REAL NOT NULL,
                is_available INTEGER NOT NULL,
                fetched_at TEXT NOT NULL,
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
            // Best effort cleanup for temp files.
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
