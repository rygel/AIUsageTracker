// <copyright file="UsageAnalyticsLogicTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

public class UsageAnalyticsLogicTests : DatabaseTestBase
{
    private readonly UsageAnalyticsService _analyticsService;

    public UsageAnalyticsLogicTests()
    {
        this._analyticsService = new UsageAnalyticsService(
            this.DatabaseService,
            this.Cache,
            NullLogger<UsageAnalyticsService>.Instance);
    }

    [Fact]
    public async Task GetProviderReliabilityAsync_CalculatesUptimeAndLatencyCorrectlyAsync()
    {
        var now = DateTime.UtcNow;
        this.SeedProvider("p1", "P1");

        for (int i = 0; i < 8; i++)
        {
            this.SeedHistory("p1", 10, 100, now.AddMinutes(-i), isAvailable: true, latencyMs: 100.0);
        }

        for (int i = 8; i < 10; i++)
        {
            this.SeedHistory("p1", 0, 0, now.AddMinutes(-i), isAvailable: false, latencyMs: 0.0);
        }

        var results = await this._analyticsService
            .GetProviderReliabilityAsync(new[] { "p1" }, 24, 100);

        Assert.True(results.TryGetValue("p1", out var stats));
        Assert.Equal(20.0, stats.FailureRatePercent);
        Assert.Equal(100.0, stats.AverageLatencyMs);
        Assert.Equal(10, stats.SampleCount);
    }
}
