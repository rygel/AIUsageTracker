using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AIConsumptionTracker.Tests.Core;

public class ProviderManagerTests
{
    private readonly Mock<ILogger<ProviderManager>> _mockLogger;
    private readonly Mock<IConfigLoader> _mockConfigLoader;
    private readonly List<IProviderService> _providers;

    public ProviderManagerTests()
    {
        _mockLogger = new Mock<ILogger<ProviderManager>>();
        _mockConfigLoader = new Mock<IConfigLoader>();
        
        var mockProvider1 = new Mock<IProviderService>();
        mockProvider1.Setup(p => p.ProviderId).Returns("test-provider-1");
        
        var mockProvider2 = new Mock<IProviderService>();
        mockProvider2.Setup(p => p.ProviderId).Returns("test-provider-2");

        _providers = new List<IProviderService> { mockProvider1.Object, mockProvider2.Object };
    }

    [Fact]
    public async Task GetAllUsageAsync_LoadsConfigAndFetchesUsage()
    {
        // Arrange
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "test-provider-1" }
        };
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(configs);
        
        var manager = new ProviderManager(_providers, _mockConfigLoader.Object, _mockLogger.Object);

        // Act
        var result = await manager.GetAllUsageAsync();

        // Assert
        Assert.NotEmpty(result);
        _mockConfigLoader.Verify(c => c.LoadConfigAsync(), Times.Once);
    }
}
