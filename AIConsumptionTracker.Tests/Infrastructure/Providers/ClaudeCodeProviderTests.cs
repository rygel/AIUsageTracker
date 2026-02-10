using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class ClaudeCodeProviderTests
{
    private readonly Mock<ILogger<ClaudeCodeProvider>> _logger;
    private readonly ClaudeCodeProvider _provider;

    public ClaudeCodeProviderTests()
    {
        _logger = new Mock<ILogger<ClaudeCodeProvider>>();
        _provider = new ClaudeCodeProvider(_logger.Object);
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
        Assert.Equal(PaymentType.UsageBased, usage.PaymentType);
    }

    [Fact]
    public async Task GetUsageAsync_WithApiKey_ShouldBeAvailableEvenIfCliFails()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "claude-code", ApiKey = "test-key" };

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert - Should be available even if CLI is not installed
        var usage = result.Single();
        Assert.True(usage.IsAvailable, "Provider should be available when API key is configured");
        Assert.Equal("Connected (API key configured)", usage.Description);
        Assert.False(usage.IsQuotaBased);
        Assert.Equal(PaymentType.UsageBased, usage.PaymentType);
    }
}
