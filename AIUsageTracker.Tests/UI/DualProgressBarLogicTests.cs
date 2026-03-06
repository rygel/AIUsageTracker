using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;
using Xunit;

namespace AIUsageTracker.Tests.UI;

public class DualProgressBarLogicTests
{
    [Theory]
    [InlineData("10%", 10.0)]
    [InlineData("45.5 %", 45.5)]
    [InlineData("100%", 100.0)]
    [InlineData("0%", 0.0)]
    [InlineData("50", 50.0)]
    [InlineData("Invalid", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParsePercent_HandlesFormatsCorrectly(string? input, double? expected)
    {
        var result = UsageMath.ParsePercent(input);
        
        if (expected == null)
            Assert.Null(result);
        else
            Assert.Equal(expected.Value, result!.Value, 1);
    }

    [Fact]
    public void GetEffectiveUsedPercent_CalculatesCorrectly()
    {
        var quotaUsage = new ProviderUsage { RequestsPercentage = 80, IsQuotaBased = true };
        var paygUsage = new ProviderUsage { RequestsPercentage = 20, IsQuotaBased = false };

        Assert.Equal(20.0, UsageMath.GetEffectiveUsedPercent(quotaUsage)); // 100 - 80
        Assert.Equal(20.0, UsageMath.GetEffectiveUsedPercent(paygUsage));
    }

    [Fact]
    public void TryGetDualWindowUsedPercentages_IdentifiesPrimaryAndSecondary()
    {
        var usage = new ProviderUsage
        {
            Details = new List<ProviderUsageDetail>
            {
                new ProviderUsageDetail 
                { 
                    Name = "Hourly", 
                    Used = "10% used", 
                    DetailType = ProviderUsageDetailType.QuotaWindow, 
                    WindowKind = WindowKind.Primary 
                },
                new ProviderUsageDetail 
                { 
                    Name = "Weekly", 
                    Used = "80% remaining", 
                    DetailType = ProviderUsageDetailType.QuotaWindow, 
                    WindowKind = WindowKind.Secondary 
                }
            }
        };

        var result = ProviderDualWindowPresentationCatalog.TryGetDualWindowUsedPercentages(
            usage,
            out var hourlyUsed,
            out var weeklyUsed);

        Assert.True(result);
        Assert.Equal(10.0, hourlyUsed);
        Assert.Equal(20.0, weeklyUsed); // 100 - 80
    }
}
