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
    public void TryGetPresentation_ReturnsLabelsAndResets_ForDualQuotaBuckets()
    {
        var weeklyReset = new DateTime(2026, 3, 12, 23, 0, 0);
        var hourlyReset = new DateTime(2026, 3, 7, 1, 0, 0);
        var burstDetail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            Description = "96% remaining (4% used)",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
            NextResetTime = hourlyReset,
        };
        burstDetail.SetPercentageValue(4.0, PercentageValueSemantic.Used);

        var rollingDetail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            Description = "49% remaining (51% used)",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
            NextResetTime = weeklyReset,
        };
        rollingDetail.SetPercentageValue(51.0, PercentageValueSemantic.Used);

        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            IsQuotaBased = true,
            Details = new List<ProviderUsageDetail> { burstDetail, rollingDetail },
        };

        var result = MainWindowRuntimeLogic.TryGetDualQuotaBucketPresentation(usage, out var presentation);

        Assert.True(result);
        Assert.Equal("5h", presentation.PrimaryLabel);
        Assert.Equal(4.0, presentation.PrimaryUsedPercent);
        Assert.Equal(hourlyReset, presentation.PrimaryResetTime);
        Assert.Equal("Weekly", presentation.SecondaryLabel);
        Assert.Equal(51.0, presentation.SecondaryUsedPercent);
        Assert.Equal(weeklyReset, presentation.SecondaryResetTime);
    }

    [Fact]
    public void TryGetPresentation_ReturnsFalse_WhenQuotaBucketKindIsMissing()
    {
        var usage = new ProviderUsage
        {
            IsQuotaBased = true,
            Details = new List<ProviderUsageDetail>
            {
                new()
                {
                    Name = "Requests / Hour",
                    Description = "80% remaining (20% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.None,
                    NextResetTime = new DateTime(2026, 3, 12, 10, 0, 0),
                },
                new()
                {
                    Name = "Requests / Day",
                    Description = "35% remaining (65% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.None,
                    NextResetTime = new DateTime(2026, 3, 12, 20, 0, 0),
                },
            },
        };

        var result = MainWindowRuntimeLogic.TryGetDualQuotaBucketPresentation(usage, out _);

        Assert.False(result);
    }
}

