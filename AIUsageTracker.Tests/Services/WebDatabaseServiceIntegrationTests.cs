using AIUsageTracker.Tests.Infrastructure;
using AIUsageTracker.Core.Models;
using Xunit;

namespace AIUsageTracker.Tests.Services;

public class WebDatabaseServiceIntegrationTests : DatabaseTestBase
{
    [Fact]
    public async Task GetUsageAnomaliesAsync_DetectsUsageSpike()
    {
        // Arrange
        var now = DateTime.UtcNow;
        SeedProvider("spike-p", "Spike Provider");

        // Baseline: 1 unit per hour for 5 hours
        for (int i = 6; i >= 1; i--)
        {
            SeedHistory("spike-p", 100 - i, 1000, now.AddHours(-i));
        }

        // Spike: 50 units in the last hour
        SeedHistory("spike-p", 150, 1000, now);

        // Act
        var anomalies = await DatabaseService.GetUsageAnomaliesAsync(new[] { "spike-p" });

        // Assert
        Assert.True(anomalies.TryGetValue("spike-p", out var snapshot));
        Assert.True(snapshot.HasAnomaly);
        Assert.Equal("Spike", snapshot.Direction);
        Assert.True(snapshot.LatestRatePerDay > snapshot.BaselineRatePerDay * 2);
    }

    [Fact]
    public async Task GetBurnRateForecastsAsync_CalculatesSteadyExhaustion()
    {
        // Arrange
        var now = DateTime.UtcNow;
        SeedProvider("steady-p", "Steady Provider");

        // Usage: 10 units per hour
        // Available: 1000 total
        for (int i = 5; i >= 0; i--)
        {
            SeedHistory("steady-p", 500 + (5 - i) * 10, 1000, now.AddHours(-i));
        }

        // Act
        var forecasts = await DatabaseService.GetBurnRateForecastsAsync(new[] { "steady-p" });

        // Assert
        Assert.True(forecasts.TryGetValue("steady-p", out var forecast));
        Assert.True(forecast.IsAvailable);
        
        // Burn rate: 10/hour = 240/day
        Assert.Equal(240.0, forecast.BurnRatePerDay, 1);
        
        // Remaining: 1000 - 550 = 450
        // Days until exhausted: 450 / 240 = 1.875 days
        Assert.Equal(1.875, forecast.DaysUntilExhausted, 1);
    }

    [Fact]
    public async Task GetUsageAnomaliesAsync_HandlesResetsCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        SeedProvider("reset-p", "Reset Provider");

        // Usage before reset: 900/1000
        SeedHistory("reset-p", 800, 1000, now.AddHours(-3));
        SeedHistory("reset-p", 900, 1000, now.AddHours(-2));
        
        // Reset happens
        SeedHistory("reset-p", 10, 1000, now.AddHours(-1));
        SeedHistory("reset-p", 20, 1000, now);

        // Act
        var anomalies = await DatabaseService.GetUsageAnomaliesAsync(new[] { "reset-p" });

        // Assert
        Assert.True(anomalies.TryGetValue("reset-p", out var snapshot));
        
        // Should not detect anomaly across reset, as TrimToLatestCycle should kick in
        // or the rates calculation should skip negative jumps.
        // If it correctly trims to the last two points, it might say "insufficient history".
        Assert.False(snapshot.HasAnomaly);
    }
}
