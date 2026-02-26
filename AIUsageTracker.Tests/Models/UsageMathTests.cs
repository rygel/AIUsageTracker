using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Tests.Models;

public class UsageMathTests
{
    [Fact]
    public void CalculateBurnRateForecast_WithPositiveTrend_ReturnsForecast()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 10, available: 100),
            CreateSample(start.AddHours(12), used: 20, available: 100),
            CreateSample(start.AddHours(24), used: 34, available: 100)
        };

        // Act
        var forecast = UsageMath.CalculateBurnRateForecast(history);

        // Assert
        Assert.True(forecast.IsAvailable);
        Assert.Equal(24, forecast.BurnRatePerDay, 3);
        Assert.Equal(66, forecast.RemainingUnits, 3);
        Assert.Equal(2.75, forecast.DaysUntilExhausted, 3);
        Assert.NotNull(forecast.EstimatedExhaustionUtc);
    }

    [Fact]
    public void CalculateBurnRateForecast_AfterReset_UsesLatestCycleOnly()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 70, available: 100),
            CreateSample(start.AddHours(10), used: 80, available: 100),
            CreateSample(start.AddHours(20), used: 5, available: 100),  // reset
            CreateSample(start.AddHours(30), used: 15, available: 100)
        };

        // Act
        var forecast = UsageMath.CalculateBurnRateForecast(history);

        // Assert
        Assert.True(forecast.IsAvailable);
        Assert.Equal(24, forecast.BurnRatePerDay, 3);
        Assert.Equal(85, forecast.RemainingUnits, 3);
        Assert.Equal(3.542, forecast.DaysUntilExhausted, 3);
    }

    [Fact]
    public void CalculateBurnRateForecast_WithInsufficientHistory_ReturnsUnavailable()
    {
        // Arrange
        var history = new List<ProviderUsage>
        {
            CreateSample(DateTime.UtcNow, used: 10, available: 100)
        };

        // Act
        var forecast = UsageMath.CalculateBurnRateForecast(history);

        // Assert
        Assert.False(forecast.IsAvailable);
        Assert.Equal("Insufficient history", forecast.Reason);
    }

    [Fact]
    public void CalculateBurnRateForecast_WithNoConsumptionTrend_ReturnsUnavailable()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 10, available: 100),
            CreateSample(start.AddHours(12), used: 10, available: 100),
            CreateSample(start.AddHours(24), used: 10, available: 100)
        };

        // Act
        var forecast = UsageMath.CalculateBurnRateForecast(history);

        // Assert
        Assert.False(forecast.IsAvailable);
        Assert.Equal("No consumption trend", forecast.Reason);
    }

    [Fact]
    public void CalculateBurnRateForecast_WithElapsedWindowBelowMinimum_ReturnsUnavailable()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 10, available: 100),
            CreateSample(start.AddMinutes(30), used: 16, available: 100),
            CreateSample(start.AddMinutes(45), used: 20, available: 100)
        };

        // Act
        var forecast = UsageMath.CalculateBurnRateForecast(history);

        // Assert
        Assert.False(forecast.IsAvailable);
        Assert.Equal("Insufficient time window", forecast.Reason);
    }

    [Fact]
    public void CalculateBurnRateForecast_WithMultipleResets_UsesLatestCycleOnly()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 70, available: 100),
            CreateSample(start.AddHours(12), used: 85, available: 100),
            CreateSample(start.AddHours(24), used: 8, available: 100),   // reset
            CreateSample(start.AddHours(36), used: 24, available: 100),
            CreateSample(start.AddHours(48), used: 2, available: 100),   // reset
            CreateSample(start.AddHours(60), used: 12, available: 100)
        };

        // Act
        var forecast = UsageMath.CalculateBurnRateForecast(history);

        // Assert
        Assert.True(forecast.IsAvailable);
        Assert.Equal(20, forecast.BurnRatePerDay, 3);
        Assert.Equal(88, forecast.RemainingUnits, 3);
        Assert.Equal(4.4, forecast.DaysUntilExhausted, 3);
        Assert.Equal(2, forecast.SampleCount);
    }

    [Fact]
    public void CalculateBurnRateForecast_WhenUsedExceedsAvailable_ClampsRemainingToZero()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 95, available: 100),
            CreateSample(start.AddHours(24), used: 110, available: 100)
        };

        // Act
        var forecast = UsageMath.CalculateBurnRateForecast(history);

        // Assert
        Assert.True(forecast.IsAvailable);
        Assert.Equal(0, forecast.RemainingUnits, 3);
        Assert.Equal(0, forecast.DaysUntilExhausted, 3);
    }

    [Fact]
    public void CalculateReliabilitySnapshot_WithMixedStatuses_ReturnsExpectedRates()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 10, available: 100, latencyMs: 400),
            CreateUnavailableSample(start.AddMinutes(5), latencyMs: 800),
            CreateSample(start.AddMinutes(10), used: 20, available: 100, latencyMs: 600),
            CreateUnavailableSample(start.AddMinutes(15), latencyMs: 1000)
        };

        // Act
        var snapshot = UsageMath.CalculateReliabilitySnapshot(history);

        // Assert
        Assert.True(snapshot.IsAvailable);
        Assert.Equal(4, snapshot.SampleCount);
        Assert.Equal(2, snapshot.SuccessCount);
        Assert.Equal(2, snapshot.FailureCount);
        Assert.Equal(50, snapshot.FailureRatePercent, 3);
        Assert.Equal(700, snapshot.AverageLatencyMs, 3);
        Assert.Equal(1000, snapshot.LastLatencyMs, 3);
        Assert.Equal(start.AddMinutes(10), snapshot.LastSuccessfulSyncUtc);
        Assert.Equal(start.AddMinutes(15), snapshot.LastSeenUtc);
    }

    [Fact]
    public void CalculateReliabilitySnapshot_WithNoHistory_ReturnsUnavailable()
    {
        // Arrange
        var history = new List<ProviderUsage>();

        // Act
        var snapshot = UsageMath.CalculateReliabilitySnapshot(history);

        // Assert
        Assert.False(snapshot.IsAvailable);
        Assert.Equal("No history", snapshot.Reason);
    }

    [Fact]
    public void CalculateReliabilitySnapshot_WithSingleSample_HasZeroAverageLatency()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 10, available: 100)
        };

        // Act
        var snapshot = UsageMath.CalculateReliabilitySnapshot(history);

        // Assert
        Assert.True(snapshot.IsAvailable);
        Assert.Equal(1, snapshot.SampleCount);
        Assert.Equal(0, snapshot.AverageLatencyMs, 3);
    }

    [Fact]
    public void CalculateReliabilitySnapshot_WithoutLatencySamples_ReturnsZeroLatency()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 10, available: 100, latencyMs: 0),
            CreateUnavailableSample(start.AddMinutes(5), latencyMs: 0)
        };

        // Act
        var snapshot = UsageMath.CalculateReliabilitySnapshot(history);

        // Assert
        Assert.True(snapshot.IsAvailable);
        Assert.Equal(0, snapshot.AverageLatencyMs, 3);
        Assert.Equal(0, snapshot.LastLatencyMs, 3);
    }

    [Fact]
    public void CalculateUsageAnomalySnapshot_WithSuddenSpike_ReturnsDetectedSpike()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 10, available: 200),
            CreateSample(start.AddHours(12), used: 20, available: 200),
            CreateSample(start.AddHours(24), used: 30, available: 200),
            CreateSample(start.AddHours(36), used: 120, available: 200)
        };

        // Act
        var snapshot = UsageMath.CalculateUsageAnomalySnapshot(history);

        // Assert
        Assert.True(snapshot.IsAvailable);
        Assert.True(snapshot.HasAnomaly);
        Assert.Equal("Spike", snapshot.Direction);
        Assert.Equal("High", snapshot.Severity);
        Assert.NotNull(snapshot.LastDetectedUtc);
    }

    [Fact]
    public void CalculateUsageAnomalySnapshot_WithSuddenDrop_ReturnsDetectedDrop()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 40, available: 100),
            CreateSample(start.AddHours(12), used: 52, available: 100),
            CreateSample(start.AddHours(24), used: 64, available: 100),
            CreateSample(start.AddHours(36), used: 56, available: 100)
        };

        // Act
        var snapshot = UsageMath.CalculateUsageAnomalySnapshot(history);

        // Assert
        Assert.True(snapshot.IsAvailable);
        Assert.True(snapshot.HasAnomaly);
        Assert.Equal("Drop", snapshot.Direction);
        Assert.Equal("High", snapshot.Severity);
    }

    [Fact]
    public void CalculateUsageAnomalySnapshot_WithStableTrend_ReturnsNoAnomaly()
    {
        // Arrange
        var start = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);
        var history = new List<ProviderUsage>
        {
            CreateSample(start, used: 10, available: 100),
            CreateSample(start.AddHours(12), used: 20, available: 100),
            CreateSample(start.AddHours(24), used: 31, available: 100),
            CreateSample(start.AddHours(36), used: 41, available: 100)
        };

        // Act
        var snapshot = UsageMath.CalculateUsageAnomalySnapshot(history);

        // Assert
        Assert.True(snapshot.IsAvailable);
        Assert.False(snapshot.HasAnomaly);
        Assert.Equal("None", snapshot.Severity);
        Assert.Null(snapshot.LastDetectedUtc);
    }

    private static ProviderUsage CreateSample(DateTime fetchedAt, double used, double available, double latencyMs = 0)
    {
        return new ProviderUsage
        {
            ProviderId = "test-provider",
            RequestsUsed = used,
            RequestsAvailable = available,
            FetchedAt = fetchedAt,
            IsAvailable = true,
            ResponseLatencyMs = latencyMs
        };
    }

    private static ProviderUsage CreateUnavailableSample(DateTime fetchedAt, double latencyMs = 0)
    {
        return new ProviderUsage
        {
            ProviderId = "test-provider",
            RequestsUsed = 0,
            RequestsAvailable = 0,
            FetchedAt = fetchedAt,
            IsAvailable = false,
            Description = "Connection failed",
            ResponseLatencyMs = latencyMs
        };
    }
}
