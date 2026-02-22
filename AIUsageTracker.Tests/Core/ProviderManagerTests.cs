using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using AIUsageTracker.Tests.Mocks;

namespace AIUsageTracker.Tests.Core;

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
            MockProviderService.CreateGeminiMock()
        };

        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "openai" },
            new ProviderConfig { ProviderId = "gemini" }
        };

        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(configs);
        
        var manager = new ProviderManager(providers, _mockConfigLoader.Object, _mockLogger.Object);

        // Act
        var result = await manager.GetAllUsageAsync();

        // Assert
        // We expect at least our 2 mocks, plus possibly auto-added system ones
        Assert.True(result.Count(r => r.IsAvailable) >= 2);
        Assert.Contains(result, r => r.PlanType == PlanType.Usage && r.ProviderId == "openai");
        Assert.Contains(result, r => r.PlanType == PlanType.Coding && r.ProviderId == "gemini");
    }

    [Fact]
    public async Task GetAllUsageAsync_FallsBackToGenericProvider()
    {
        // Arrange
        var genericMock = new MockProviderService
        {
            ProviderId = "generic-pay-as-you-go",
            UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Fallback Provider",
                IsAvailable = true,
                Description = "Generic Fallback"
            }})
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

    [Fact]
    public async Task GetAllUsageAsync_WhenIncludeProviderIdsProvided_FetchesOnlyIncludedProviders()
    {
        // Arrange
        var providers = new List<IProviderService>
        {
            MockProviderService.CreateOpenAIMock(),
            MockProviderService.CreateGeminiMock()
        };

        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "openai" },
            new ProviderConfig { ProviderId = "gemini" }
        };

        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(configs);
        var manager = new ProviderManager(providers, _mockConfigLoader.Object, _mockLogger.Object);

        // Act
        var result = await manager.GetAllUsageAsync(
            includeProviderIds: new[] { "openai" });

        // Assert
        Assert.Contains(result, r => r.ProviderId == "openai");
        Assert.DoesNotContain(result, r => r.ProviderId == "gemini");
    }
}

