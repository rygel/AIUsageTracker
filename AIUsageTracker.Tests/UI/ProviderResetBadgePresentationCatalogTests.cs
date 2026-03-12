// <copyright file="ProviderResetBadgePresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public class ProviderResetBadgePresentationCatalogTests
{
    [Fact]
    public void ResolveResetTimes_ReturnsDualBucketResets_BeforeSingleFallback()
    {
        var now = DateTime.UtcNow;
        var usage = new ProviderUsage
        {
            IsQuotaBased = true,
            NextResetTime = now.AddHours(8),
            Details = new List<ProviderUsageDetail>
            {
                new()
                {
                    Name = "5-hour quota",
                    Used = "80% remaining (20% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Primary,
                    NextResetTime = now.AddHours(1),
                },
                new()
                {
                    Name = "Weekly quota",
                    Used = "65% remaining (35% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Secondary,
                    NextResetTime = now.AddDays(2),
                },
            },
        };

        var resetTimes = ProviderResetBadgePresentationCatalog.ResolveResetTimes(usage, suppressSingleResetFallback: false);

        Assert.Equal(2, resetTimes.Count);
        Assert.Contains(now.AddHours(1), resetTimes);
        Assert.Contains(now.AddDays(2), resetTimes);
        Assert.DoesNotContain(now.AddHours(8), resetTimes);
    }

    [Fact]
    public void ResolveResetTimes_SkipsSingleFallback_WhenSuppressedAndNoDualResets()
    {
        var usage = new ProviderUsage
        {
            NextResetTime = DateTime.UtcNow.AddHours(3),
            Details = new List<ProviderUsageDetail>(),
        };

        var resetTimes = ProviderResetBadgePresentationCatalog.ResolveResetTimes(usage, suppressSingleResetFallback: true);

        Assert.Empty(resetTimes);
    }

    [Fact]
    public void ResolveResetTimes_UsesSingleFallback_WhenNotSuppressed()
    {
        var nextReset = DateTime.UtcNow.AddHours(3);
        var usage = new ProviderUsage
        {
            NextResetTime = nextReset,
            Details = new List<ProviderUsageDetail>(),
        };

        var resetTimes = ProviderResetBadgePresentationCatalog.ResolveResetTimes(usage, suppressSingleResetFallback: false);

        var onlyReset = Assert.Single(resetTimes);
        Assert.Equal(nextReset, onlyReset);
    }
}
