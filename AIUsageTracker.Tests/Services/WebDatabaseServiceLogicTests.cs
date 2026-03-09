// <copyright file="WebDatabaseServiceLogicTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Services
{
    using AIUsageTracker.Tests.Infrastructure;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Infrastructure.Services;
    using Microsoft.Extensions.Logging.Abstractions;
    using Xunit;

    public class UsageAnalyticsLogicTests : DatabaseTestBase
    {
        private readonly UsageAnalyticsService _analyticsService;

        public UsageAnalyticsLogicTests()
        {
            this._analyticsService = new UsageAnalyticsService(this.DatabaseService, this.Cache, NullLogger<UsageAnalyticsService>.Instance);
        }

        [Fact]
        public async Task GetProviderReliabilityAsync_CalculatesUptimeAndLatencyCorrectly()
        {
            // Arrange
            var now = DateTime.UtcNow;
            this.SeedProvider("p1", "P1");

            // Seed 10 rows: 8 available, 2 unavailable. 
            for (int i = 0; i < 8; i++)
            {
                this.SeedHistory("p1", 10, 100, now.AddMinutes(-i), isAvailable: true, latencyMs: 100.0);
            }
            for (int i = 8; i < 10; i++)
            {
                this.SeedHistory("p1", 0, 0, now.AddMinutes(-i), isAvailable: false, latencyMs: 0.0);
            }

            // Act
            var results = await this._analyticsService.GetProviderReliabilityAsync(new[] { "p1" }, 24, 100);

            // Assert
            Assert.True(results.TryGetValue("p1", out var stats));
            Assert.Equal(20.0, stats.FailureRatePercent); // 2/10
            Assert.Equal(100.0, stats.AverageLatencyMs);
            Assert.Equal(10, stats.SampleCount);
        }
    }
    `n
    public class WebDatabaseServiceLogicTests : DatabaseTestBase
    {
        [Fact]
        public async Task GetLatestUsageAsync_ReturnsMostRecentRowsPerProvider()
        {
            // Arrange
            var now = DateTime.UtcNow;
            this.SeedProvider("openai", "OpenAI");
            this.SeedProvider("anthropic", "Anthropic");

            // Older rows
            this.SeedHistory("openai", 10, 100, now.AddHours(-2));
            this.SeedHistory("anthropic", 5, 50, now.AddHours(-2));

            // Most recent rows
            this.SeedHistory("openai", 20, 100, now.AddHours(-1));
            this.SeedHistory("anthropic", 15, 50, now.AddHours(-1));

            // Act
            var results = await this.DatabaseService.GetLatestUsageAsync();

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
            this.SeedProvider("p1", "Provider 1", "account1", isActive: true);
            this.SeedProvider("p2", "Provider 2", "account2", isActive: false);

            // Act
            var providers = await this.DatabaseService.GetProvidersAsync();

            // Assert
            Assert.Single(providers);
            Assert.Equal("p1", providers[0].ProviderId);
        }

        [Fact]
        public async Task GetUsageSummaryAsync_AggregatesTotalsCorrectly()
        {
            // Arrange
            var now = DateTime.UtcNow;
            this.SeedProvider("p1", "P1");
            this.SeedProvider("p2", "P2");

            // p1: 10% used -> 90% remaining (requests_percentage = 90)
            this.SeedHistory("p1", 10, 100, now);
            // p2: 50% used -> 50% remaining (requests_percentage = 50)
            this.SeedHistory("p2", 50, 100, now);

            // Act
            var summary = await this.DatabaseService.GetUsageSummaryAsync();

            // Assert
            Assert.Equal(2, summary.ProviderCount);
            Assert.Equal(70.0, summary.AverageUsage);
        }
    }
}
