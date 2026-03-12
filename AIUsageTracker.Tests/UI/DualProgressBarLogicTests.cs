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
        var quotaUsage = new ProviderUsage { RequestsPercentage = 80, IsQuotaBased = true };
        var paygUsage = new ProviderUsage { RequestsPercentage = 20, IsQuotaBased = false };

        Assert.Equal(20.0, UsageMath.GetEffectiveUsedPercent(quotaUsage)); // 100 - 80
        Assert.Equal(20.0, UsageMath.GetEffectiveUsedPercent(paygUsage));
    }

    [Fact]
    public void TryGetDualQuotaBucketUsedPercentages_IdentifiesPrimaryAndSecondary()
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
                    QuotaBucketKind = WindowKind.Primary,
                },
                new ProviderUsageDetail
                {
                    Name = "Weekly",
                    Used = "80% remaining",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Secondary,
                },
            },
        };

        var result = ProviderDualQuotaBucketPresentationCatalog.TryGetDualQuotaBucketUsedPercentages(
            usage,
            out var primaryUsed,
            out var secondaryUsed);

        Assert.True(result);
        Assert.Equal(10.0, primaryUsed);
        Assert.Equal(20.0, secondaryUsed); // 100 - 80
    }

    [Fact]
    public void TryGetPresentation_ReturnsLabelsAndResets_ForDualQuotaBuckets()
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
                    QuotaBucketKind = WindowKind.Primary,
                    NextResetTime = hourlyReset,
                },
                new ProviderUsageDetail
                {
                    Name = "Weekly quota",
                    Used = "49% remaining (51% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Secondary,
                    NextResetTime = weeklyReset,
                },
            },
        };

        var result = ProviderDualQuotaBucketPresentationCatalog.TryGetPresentation(usage, out var presentation);

        Assert.True(result);
        Assert.Equal("5-hour", presentation.PrimaryLabel);
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
                    Used = "80% remaining (20% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.None,
                    NextResetTime = new DateTime(2026, 3, 12, 10, 0, 0),
                },
                new()
                {
                    Name = "Requests / Day",
                    Used = "35% remaining (65% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.None,
                    NextResetTime = new DateTime(2026, 3, 12, 20, 0, 0),
                },
            },
        };

        var result = ProviderDualQuotaBucketPresentationCatalog.TryGetPresentation(usage, out _);

        Assert.False(result);
    }
}
