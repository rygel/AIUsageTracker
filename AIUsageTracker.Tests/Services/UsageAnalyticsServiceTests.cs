using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Services;

public class UsageAnalyticsServiceTests
{
    private readonly Mock<IWebDatabaseRepository> _mockRepo = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task GetBurnRateForecastsAsync_WithEmptyProviderIds_ReturnsEmpty()
    {
        var service = new UsageAnalyticsService(_mockRepo.Object, _cache, NullLogger<UsageAnalyticsService>.Instance);
        var result = await service.GetBurnRateForecastsAsync(Enumerable.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProviderReliabilityAsync_WithEmptyProviderIds_ReturnsEmpty()
    {
        var service = new UsageAnalyticsService(_mockRepo.Object, _cache, NullLogger<UsageAnalyticsService>.Instance);
        var result = await service.GetProviderReliabilityAsync(Enumerable.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsageAnomaliesAsync_WithEmptyProviderIds_ReturnsEmpty()
    {
        var service = new UsageAnalyticsService(_mockRepo.Object, _cache, NullLogger<UsageAnalyticsService>.Instance);
        var result = await service.GetUsageAnomaliesAsync(Enumerable.Empty<string>());
        Assert.Empty(result);
    }
}
