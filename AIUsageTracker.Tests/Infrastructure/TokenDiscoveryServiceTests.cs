using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public class TokenDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverTokensAsync_IncludesCodexAsWellKnownProvider()
    {
        // Arrange
        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(Path.GetTempPath());
        var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

        // Act
        var configs = await discovery.DiscoverTokensAsync();

        // Assert
        var codex = configs.FirstOrDefault(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(codex);
        Assert.Equal(PlanType.Coding, codex!.PlanType);
        Assert.Equal("quota-based", codex.Type);
    }

    [Fact]
    public async Task DiscoverTokensAsync_DoesNotExpandWellKnownProviderAliases()
    {
        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(Path.GetTempPath());
        var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

        var configs = await discovery.DiscoverTokensAsync();

        Assert.DoesNotContain(configs, c => c.ProviderId.Equals("minimax-io", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(configs, c => c.ProviderId.Equals("minimax-global", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(configs, c => c.ProviderId.Equals("kimi-for-coding", StringComparison.OrdinalIgnoreCase));
    }
}
