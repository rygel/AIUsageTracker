using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class AnthropicProviderTests
{
    private readonly AnthropicProvider _provider;

    public AnthropicProviderTests()
    {
        var logger = new Mock<ILogger<AnthropicProvider>>();
        _provider = new AnthropicProvider(logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_WhenApiKeyMissing_PopulatesRawSnapshotFields()
    {
        var config = new ProviderConfig { ProviderId = "anthropic", ApiKey = "" };

        var usage = (await _provider.GetUsageAsync(config)).Single();

        Assert.False(usage.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(usage.RawJson));
        Assert.Equal(401, usage.HttpStatus);
    }

    [Fact]
    public async Task GetUsageAsync_WhenApiKeyConfigured_PopulatesRawSnapshotFields()
    {
        var config = new ProviderConfig { ProviderId = "anthropic", ApiKey = "key" };

        var usage = (await _provider.GetUsageAsync(config)).Single();

        Assert.True(usage.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(usage.RawJson));
        Assert.Equal(200, usage.HttpStatus);
    }
}
