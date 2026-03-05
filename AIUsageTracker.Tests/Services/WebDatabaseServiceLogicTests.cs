using AIUsageTracker.Tests.Infrastructure;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AIUsageTracker.Tests.Services;

public class WebDatabaseServiceLogicTests : DatabaseTestBase
{
    [Fact]
    public async Task GetLatestUsageAsync_ReturnsMostRecentRowsPerProvider()
    {
        // Arrange
        var now = DateTime.UtcNow;
        SeedProvider("openai", "OpenAI");
        SeedProvider("anthropic", "Anthropic");

        // Older rows
        SeedHistory("openai", 10, 100, now.AddHours(-2));
        SeedHistory("anthropic", 5, 50, now.AddHours(-2));

        // Most recent rows
        SeedHistory("openai", 20, 100, now.AddHours(-1));
        SeedHistory("anthropic", 15, 50, now.AddHours(-1));

        // Act
        var results = await DatabaseService.GetLatestUsageAsync();

        // Assert
        Assert.Equal(2, results.Count);
        
        var openai = results.First(r => r.ProviderId == "openai");
        Assert.Equal(20, openai.RequestsUsed);
        
        var anthropic = results.First(r => r.ProviderId == "anthropic");
        Assert.Equal(15, anthropic.RequestsUsed);
    }

    [Fact]
    public async Task GetProvidersAsync_ReturnsActiveProvidersOnly()
    {
        // Arrange
        SeedProvider("p1", "Provider 1", "account1", isActive: true);
        SeedProvider("p2", "Provider 2", "account2", isActive: false);

        // Act
        var providers = await DatabaseService.GetProvidersAsync();

        // Assert
        // The SQL in GetProvidersAsync has: WHERE p.is_active = 1
        Assert.Single(providers);
        Assert.Equal("p1", providers[0].ProviderId);
    }

    [Fact]
    public async Task GetLatestUsageAsync_RespectsIncludeInactiveFlag_BasedOnAvailability()
    {
        // Arrange
        var now = DateTime.UtcNow;
        SeedProvider("p1", "P1");
        SeedProvider("p2", "P2");

        // p1 is available, p2 is NOT available
        SeedHistory("p1", 10, 100, now, isAvailable: true);
        SeedHistory("p2", 20, 100, now, isAvailable: false);

        // Act
        var activeOnly = await DatabaseService.GetLatestUsageAsync(includeInactive: false);
        var all = await DatabaseService.GetLatestUsageAsync(includeInactive: true);

        // Assert
        // GetLatestUsageAsync(includeInactive: false) uses: WHERE ... AND h.is_available = 1
        Assert.Single(activeOnly);
        Assert.Equal("p1", activeOnly[0].ProviderId);
        
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task ExportHistoryToCsvAsync_ReturnsValidCsv()
    {
        // Arrange
        SeedProvider("csv-p", "CSV Provider");
        SeedHistory("csv-p", 10, 100, DateTime.UtcNow);

        // Act
        var csv = await DatabaseService.ExportHistoryToCsvAsync();

        // Assert
        Assert.NotNull(csv);
        Assert.NotEmpty(csv);
        Assert.Contains("provider_id,provider_name", csv);
        Assert.Contains("csv-p", csv);
    }

    [Fact]
    public async Task GetProviderReliabilityAsync_CalculatesUptimeAndLatencyCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        SeedProvider("p1", "P1");

        // Seed 10 rows: 8 available, 2 unavailable. 
        for (int i = 0; i < 8; i++)
        {
            SeedHistory("p1", 10, 100, now.AddMinutes(-i), isAvailable: true, latencyMs: 100.0);
        }
        for (int i = 8; i < 10; i++)
        {
            SeedHistory("p1", 0, 0, now.AddMinutes(-i), isAvailable: false, latencyMs: 0.0);
        }

        // Act
        var results = await DatabaseService.GetProviderReliabilityAsync(new[] { "p1" }, 24, 100);

        // Assert
        Assert.True(results.TryGetValue("p1", out var stats));
        Assert.Equal(20.0, stats.FailureRatePercent); // 2/10
        Assert.Equal(100.0, stats.AverageLatencyMs);
        Assert.Equal(10, stats.SampleCount);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_AggregatesTotalsCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        SeedProvider("p1", "P1");
        SeedProvider("p2", "P2");

        // p1: 10% used -> 90% remaining (requests_percentage = 90)
        SeedHistory("p1", 10, 100, now);
        // p2: 50% used -> 50% remaining (requests_percentage = 50)
        SeedHistory("p2", 50, 100, now);

        // Act
        var summary = await DatabaseService.GetUsageSummaryAsync();

        // Assert
        Assert.Equal(2, summary.ProviderCount);
        // Average utilization (Average of percentages in DB): (90 + 50) / 2 = 70%
        Assert.Equal(70.0, summary.AverageUsage);
    }
}
