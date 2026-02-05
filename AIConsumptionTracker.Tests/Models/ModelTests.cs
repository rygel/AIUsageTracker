using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Tests.Models;

public class ModelTests
{
    [Fact]
    public void ProviderUsage_Initialization_SetsDefaultValues()
    {
        // Arrange & Act
        var usage = new ProviderUsage();

        // Assert
        Assert.Equal(PaymentType.UsageBased, usage.PaymentType);
        Assert.False(usage.IsQuotaBased);
        Assert.True(usage.IsAvailable);
        Assert.Empty(usage.Description);
    }

    [Fact]
    public void ProviderConfig_Initialization_SetsDefaultValues()
    {
        // Arrange & Act
        var config = new ProviderConfig();

        // Assert
        Assert.Empty(config.ApiKey);
        Assert.Equal("pay-as-you-go", config.Type);
    }
}
