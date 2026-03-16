// <copyright file="WebDatabaseServiceLogicTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Tests.Infrastructure;

namespace AIUsageTracker.Tests.Services;

public class WebDatabaseServiceLogicTests : DatabaseTestBase
{
    [Fact]
    public async Task GetLatestUsageAsync_ReturnsMostRecentRowsPerProviderAsync()
    {
        var now = DateTime.UtcNow;
        this.SeedProvider("openai", "OpenAI");
        this.SeedProvider("anthropic", "Anthropic");

        this.SeedHistory("openai", 10, 100, now.AddHours(-2));
        this.SeedHistory("anthropic", 5, 50, now.AddHours(-2));

        this.SeedHistory("openai", 20, 100, now.AddHours(-1));
        this.SeedHistory("anthropic", 15, 50, now.AddHours(-1));

        var results = await this.DatabaseService.GetLatestUsageAsync();

        Assert.Equal(2, results.Count);

        var openAi = results.First(result => string.Equals(result.ProviderId, "openai", StringComparison.Ordinal));
        Assert.Equal(20, openAi.RequestsUsed);

        var anthropic = results.First(result => string.Equals(result.ProviderId, "anthropic", StringComparison.Ordinal));
        Assert.Equal(15, anthropic.RequestsUsed);
    }

    [Fact]
    public async Task GetProvidersAsync_ReturnsActiveProvidersOnlyAsync()
    {
        this.SeedProvider("p1", "Provider 1", "account1", isActive: true);
        this.SeedProvider("p2", "Provider 2", "account2", isActive: false);

        var providers = await this.DatabaseService.GetProvidersAsync();

        Assert.Single(providers);
        Assert.Equal("p1", providers[0].ProviderId);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_AggregatesTotalsCorrectlyAsync()
    {
        var now = DateTime.UtcNow;
        this.SeedProvider("p1", "P1");
        this.SeedProvider("p2", "P2");

        this.SeedHistory("p1", 10, 100, now);
        this.SeedHistory("p2", 50, 100, now);

        var summary = await this.DatabaseService.GetUsageSummaryAsync();

        Assert.Equal(2, summary.ProviderCount);
        Assert.Equal(70.0, summary.AverageUsage);
    }
}
