using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using AIConsumptionTracker.Tests.Mocks;

namespace AIConsumptionTracker.Tests.Core;

public class ProviderManagerTests
{
    private readonly Mock<ILogger<ProviderManager>> _mockLogger;
    private readonly Mock<IConfigLoader> _mockConfigLoader;

    public ProviderManagerTests()
    {
        _mockLogger = new Mock<ILogger<ProviderManager>>();
        _mockConfigLoader = new Mock<IConfigLoader>();
    }

    [Fact]
    public async Task GetAllUsageAsync_LoadsConfigAndFetchesUsageFromMocks()
    {
        // Arrange
        var providers = new List<IProviderService> 
        { 
            MockProviderService.CreateOpenAIMock(),
            MockProviderService.CreateAnthropicMock(),
            MockProviderService.CreateGeminiMock()
        };

        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "openai" },
            new ProviderConfig { ProviderId = "anthropic" },
            new ProviderConfig { ProviderId = "gemini" }
        };

        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(configs);
        
        var manager = new ProviderManager(providers, _mockConfigLoader.Object, _mockLogger.Object);

        // Act
        var result = await manager.GetAllUsageAsync();

        // Assert
        // We expect at least our 3 mocks, plus possibly auto-added system ones
        Assert.True(result.Count(r => r.IsAvailable) >= 3);
        Assert.Contains(result, r => r.PaymentType == PaymentType.UsageBased && r.ProviderId == "openai");
        Assert.Contains(result, r => r.PaymentType == PaymentType.Credits && r.ProviderId == "anthropic");
        Assert.Contains(result, r => r.PaymentType == PaymentType.Quota && r.ProviderId == "gemini");
    }

    [Fact]
    public async Task GetAllUsageAsync_FallsBackToGenericProvider()
    {
        // Arrange
        var genericMock = new MockProviderService
        {
            ProviderId = "generic-pay-as-you-go",
            UsageHandler = config => Task.FromResult(new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Fallback Provider",
                IsAvailable = true,
                Description = "Generic Fallback"
            })
        };

        var providers = new List<IProviderService> { genericMock };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "unknown-api", Type = "api" }
        };

        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(configs);
        var manager = new ProviderManager(providers, _mockConfigLoader.Object, _mockLogger.Object);

        // Act
        var result = await manager.GetAllUsageAsync();

        // Assert
        Assert.Contains(result, r => r.ProviderId == "unknown-api" && r.Description == "Generic Fallback");
    }
}
