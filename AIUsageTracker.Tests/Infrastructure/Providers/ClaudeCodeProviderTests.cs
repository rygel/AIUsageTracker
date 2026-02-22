using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class ClaudeCodeProviderTests
{
    private readonly Mock<ILogger<ClaudeCodeProvider>> _logger;
    private readonly HttpClient _httpClient;
    private readonly ClaudeCodeProvider _provider;

    public ClaudeCodeProviderTests()
    {
        _logger = new Mock<ILogger<ClaudeCodeProvider>>();
        _httpClient = new HttpClient();
        _provider = new ClaudeCodeProvider(_logger.Object, _httpClient);
    }

    [Fact]
    public async Task GetUsageAsync_WhenNoApiKey_ReturnsNotAvailable()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "claude-code", ApiKey = "" };

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert
        var usage = result.Single();
        Assert.Equal("claude-code", usage.ProviderId);
        Assert.False(usage.IsAvailable);
        Assert.Equal("No API key configured", usage.Description);
        Assert.False(usage.IsQuotaBased);
        Assert.Equal(PlanType.Usage, usage.PlanType);
    }

    [Fact]
    public async Task GetUsageAsync_WithApiKey_ShouldBeAvailableEvenIfApiFails()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "claude-code", ApiKey = "test-key" };

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert - Should be available even if API/CLI fails
        var usage = result.Single();
        Assert.True(usage.IsAvailable, "Provider should be available when API key is configured");
        Assert.False(usage.IsQuotaBased);
        Assert.Equal(PlanType.Usage, usage.PlanType);
    }
}

