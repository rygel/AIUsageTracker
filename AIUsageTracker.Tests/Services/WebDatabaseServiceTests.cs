using AIUsageTracker.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

public class WebDatabaseServiceTests
{
    [Fact]
    public async Task GetBurnRateForecastsAsync_WithNonPositiveLookbackHours_Throws()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new WebDatabaseService(cache, NullLogger<WebDatabaseService>.Instance);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.GetBurnRateForecastsAsync(new[] { "openai" }, lookbackHours: 0, maxSamplesPerProvider: 720));
    }

    [Fact]
    public async Task GetBurnRateForecastsAsync_WithNonPositiveMaxSamples_Throws()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new WebDatabaseService(cache, NullLogger<WebDatabaseService>.Instance);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.GetBurnRateForecastsAsync(new[] { "openai" }, lookbackHours: 72, maxSamplesPerProvider: 0));
    }
}
