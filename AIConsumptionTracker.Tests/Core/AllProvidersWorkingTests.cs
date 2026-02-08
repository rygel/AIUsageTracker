using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIConsumptionTracker.Tests.Core;

public class AllProvidersWorkingTests
{
    private readonly Mock<IConfigLoader> _mockConfigLoader;
    private readonly Mock<ILogger<ProviderManager>> _mockLogger;

    public AllProvidersWorkingTests()
    {
        _mockConfigLoader = new Mock<IConfigLoader>();
        _mockLogger = new Mock<ILogger<ProviderManager>>();
    }

    [Fact]
    public async Task GetAllUsageAsync_ShouldIncludeBothMinimaxVariants()
    {
        // Arrange
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "minimax", ApiKey = "dummy-china" },
            new ProviderConfig { ProviderId = "minimax-io", ApiKey = "dummy-intl" }
        };
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(configs);

        // We need a real MinimaxProvider or a mock that respects the ID
        var mockMinimax = new Mock<IProviderService>();
        mockMinimax.Setup(p => p.ProviderId).Returns("minimax");
        mockMinimax.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync((ProviderConfig c, Action<ProviderUsage>? callback) => new[] { new ProviderUsage {
                ProviderId = c.ProviderId,
                ProviderName = "Minimax",
                IsAvailable = true
            }});

        var providers = new List<IProviderService> { mockMinimax.Object };
        var manager = new ProviderManager(providers, _mockConfigLoader.Object, _mockLogger.Object);

        // Act
        var results = await manager.GetAllUsageAsync();

        // Assert
        Assert.Contains(results, r => r.ProviderId == "minimax");
        Assert.Contains(results, r => r.ProviderId == "minimax-io");
    }

}
