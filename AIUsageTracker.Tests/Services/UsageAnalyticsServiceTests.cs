// <copyright file="UsageAnalyticsServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Services
{
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Infrastructure.Services;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Logging.Abstractions;
    using Moq;
    using Xunit;

    public class UsageAnalyticsServiceTests
    {
        private readonly Mock<IWebDatabaseRepository> _mockRepo = new();
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        [Fact]
        public async Task GetBurnRateForecastsAsync_WithEmptyProviderIds_ReturnsEmpty()
        {
            var service = new UsageAnalyticsService(this._mockRepo.Object, this._cache, NullLogger<UsageAnalyticsService>.Instance);
            var result = await service.GetBurnRateForecastsAsync(Enumerable.Empty<string>());
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetProviderReliabilityAsync_WithEmptyProviderIds_ReturnsEmpty()
        {
            var service = new UsageAnalyticsService(this._mockRepo.Object, this._cache, NullLogger<UsageAnalyticsService>.Instance);
            var result = await service.GetProviderReliabilityAsync(Enumerable.Empty<string>());
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetUsageAnomaliesAsync_WithEmptyProviderIds_ReturnsEmpty()
        {
            var service = new UsageAnalyticsService(this._mockRepo.Object, this._cache, NullLogger<UsageAnalyticsService>.Instance);
            var result = await service.GetUsageAnomaliesAsync(Enumerable.Empty<string>());
            Assert.Empty(result);
        }
    }
}
