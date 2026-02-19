using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class AntigravityProviderTests
{
    private readonly Mock<ILogger<AntigravityProvider>> _logger;
    private readonly AntigravityProvider _provider;

    public AntigravityProviderTests()
    {
        _logger = new Mock<ILogger<AntigravityProvider>>();
        _provider = new AntigravityProvider(_logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_WhenNotRunning_ReturnsQuotaPlanType()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "antigravity", ApiKey = "" };

        // Act - Antigravity is not running, so it will return "Not running"
        var result = await _provider.GetUsageAsync(config);

        // Assert
        // Use First() instead of Single() because on a dev machine with the app running, 
        // it might return actual process data.
        var usage = result.First();
        Console.WriteLine($"DEBUG: ProviderId={usage.ProviderId}, IsQuotaBased={usage.IsQuotaBased}, PlanType={usage.PlanType}, Description={usage.Description}");
        Assert.Equal("antigravity", usage.ProviderId);
        Assert.True(usage.IsQuotaBased, "Antigravity should be quota-based even when not running");
        Assert.Equal(PlanType.Coding, usage.PlanType);
    }

    [Fact]
    public async Task GetUsageAsync_Result_ShouldAlwaysHaveQuotaPlanType()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "antigravity", ApiKey = "" };

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert - All returned usages should have Quota payment type
        Assert.All(result, usage =>
        {
            Assert.True(usage.IsQuotaBased, $"Provider {usage.ProviderId} should have IsQuotaBased=true");
            Assert.Equal(PlanType.Coding, usage.PlanType);
        });
    }
}
