using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class AnthropicProviderTests
{
    private readonly AnthropicProvider _provider = new();
    private readonly ProviderConfig Config = new() { ProviderId = "anthropic", AuthSource = "test" };

    [Fact]
    public async Task GetUsageAsync_WhenApiKeyMissing_ReturnsUnavailable()
    {
        Config.ApiKey = "";
        
        var usage = (await _provider.GetUsageAsync(Config)).Single();

        Assert.False(usage.IsAvailable);
        Assert.Equal(401, usage.HttpStatus);
    }

    [Fact]
    public async Task GetUsageAsync_WhenApiKeyConfigured_ReturnsConnected()
    {
        Config.ApiKey = "test-key";
        
        var usage = (await _provider.GetUsageAsync(Config)).Single();

        Assert.True(usage.IsAvailable);
        Assert.Equal(200, usage.HttpStatus);
        Assert.Equal("Connected (Check Dashboard)", usage.Description);
    }
}
