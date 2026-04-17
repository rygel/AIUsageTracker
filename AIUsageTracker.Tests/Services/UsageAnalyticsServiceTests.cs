// <copyright file="UsageAnalyticsServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Services;

public class UsageAnalyticsServiceTests
{
    private readonly Mock<IWebDatabaseRepository> _mockRepo = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    [Theory]
    [InlineData("BurnRate")]
    [InlineData("Reliability")]
    [InlineData("Anomalies")]
    public async Task AnalyticsMethod_WithEmptyProviderIds_ReturnsEmptyAsync(string method)
    {
        var service = new UsageAnalyticsService(this._mockRepo.Object, this._cache, NullLogger<UsageAnalyticsService>.Instance);
        var empty = Enumerable.Empty<string>();

        var count = method switch
        {
            "BurnRate" => (await service.GetBurnRateForecastsAsync(empty)).Count(),
            "Reliability" => (await service.GetProviderReliabilityAsync(empty)).Count(),
            _ => (await service.GetUsageAnomaliesAsync(empty)).Count(),
        };

        Assert.Equal(0, count);
    }
}
