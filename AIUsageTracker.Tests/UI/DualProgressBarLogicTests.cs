// <copyright file="DualProgressBarLogicTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
        {
            Assert.Null(result);
        }
        else
        {
            Assert.Equal(expected.Value, result!.Value, 1);
        }
    }

    [Fact]
    public void GetEffectiveUsedPercent_CalculatesCorrectly()
    {
        var quotaUsage = new ProviderUsage { UsedPercent = 80, IsQuotaBased = true };
        var paygUsage = new ProviderUsage { UsedPercent = 20, IsQuotaBased = false };

        Assert.Equal(80.0, UsageMath.GetEffectiveUsedPercent(quotaUsage)); // UsedPercent is the used ratio
        Assert.Equal(20.0, UsageMath.GetEffectiveUsedPercent(paygUsage));
    }

    [Fact]
    public void TryGetPresentation_ReturnsFalse_ForProviderWithoutDeclaredQuotaWindows()
    {
        // Dual-window data comes from flat ProviderUsage cards, not from Details.
        // A plain provider usage without declared quota windows returns false.
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            IsQuotaBased = true,
        };

        var result = MainWindowRuntimeLogic.TryGetDualQuotaBucketPresentation(usage, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryGetPresentation_ReturnsFalse_WhenProviderHasNoDeclaredQuotaWindows()
    {
        var usage = new ProviderUsage
        {
            IsQuotaBased = true,
        };

        var result = MainWindowRuntimeLogic.TryGetDualQuotaBucketPresentation(usage, out _);

        Assert.False(result);
    }
}
