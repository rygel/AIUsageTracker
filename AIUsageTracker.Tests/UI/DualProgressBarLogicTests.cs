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
    [InlineData("96% remaining (4% used)", 4.0)]
    [InlineData("49% remaining (51% used)", 51.0)]
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

    [Fact]
    public void TryGetPresentation_ReturnsLabelsAndResets_ForDualWindows()
    {
        var weeklyReset = new DateTime(2026, 3, 12, 23, 0, 0);
        var hourlyReset = new DateTime(2026, 3, 7, 1, 0, 0);
        var usage = new ProviderUsage
        {
            Details = new List<ProviderUsageDetail>
            {
                new ProviderUsageDetail
                {
                    Name = "5-hour quota",
                    Used = "96% remaining (4% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    WindowKind = WindowKind.Primary,
                    NextResetTime = hourlyReset
                },
                new ProviderUsageDetail
                {
                    Name = "Weekly quota",
                    Used = "49% remaining (51% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    WindowKind = WindowKind.Secondary,
                    NextResetTime = weeklyReset
                }
            }
        };

        var result = ProviderDualWindowPresentationCatalog.TryGetPresentation(usage, out var presentation);

        Assert.True(result);
        Assert.Equal("5-hour", presentation.PrimaryLabel);
        Assert.Equal(4.0, presentation.PrimaryUsedPercent);
        Assert.Equal(hourlyReset, presentation.PrimaryResetTime);
        Assert.Equal("Weekly", presentation.SecondaryLabel);
        Assert.Equal(51.0, presentation.SecondaryUsedPercent);
        Assert.Equal(weeklyReset, presentation.SecondaryResetTime);
    }
}
