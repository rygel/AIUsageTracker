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
                    QuotaBucketKind = WindowKind.Burst,
                    NextResetTime = now.AddHours(1),
                },
                new()
                {
                    Name = "Weekly quota",
                    Used = "65% remaining (35% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Rolling,
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

    // ── Typed-percentage path (regression guard for beta.39 NormalizeDetails bug) ──
    // Providers like Codex set PercentageValue but leave the Used string empty.
    // After NormalizeDetails, PercentageValue must survive so TryGetPresentation
    // succeeds and both reset times are returned — not just the burst fallback.

    [Fact]
    public void ResolveResetTimes_ReturnsBothTimes_WhenDetailsUseTypedPercentageOnly()
    {
        // Arrange: quota window details with PercentageValue but NO Used string.
        // This is how CodexProvider emits them; the legacy Used path would fail.
        var now = DateTime.UtcNow;
        var burstReset = now.AddHours(2);
        var weeklyReset = now.AddDays(6);

        var burstDetail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
            NextResetTime = burstReset,
        };
        burstDetail.SetPercentageValue(80.0, PercentageValueSemantic.Remaining);

        var rollingDetail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
            NextResetTime = weeklyReset,
        };
        rollingDetail.SetPercentageValue(55.0, PercentageValueSemantic.Remaining);

        var usage = new ProviderUsage
        {
            IsQuotaBased = true,
            NextResetTime = burstReset, // provider sets this to burst; should NOT override dual result
            Details = new List<ProviderUsageDetail> { burstDetail, rollingDetail },
        };

        var resetTimes = ProviderResetBadgePresentationCatalog.ResolveResetTimes(usage, suppressSingleResetFallback: false);

        Assert.Equal(2, resetTimes.Count);
        Assert.Contains(burstReset, resetTimes);
        Assert.Contains(weeklyReset, resetTimes);
    }

    [Fact]
    public void ResolveResetTimes_FallsBackToBurstOnly_WhenPercentageValueIsNull()
    {
        // This documents the pre-beta.39 behaviour: when NormalizeDetails dropped
        // PercentageValue, TryGetPresentation returned false and only usage.NextResetTime
        // (the burst reset) was returned — the weekly reset was lost.
        var now = DateTime.UtcNow;
        var burstReset = now.AddHours(2);
        var weeklyReset = now.AddDays(6);

        var usage = new ProviderUsage
        {
            IsQuotaBased = true,
            NextResetTime = burstReset,
            Details = new List<ProviderUsageDetail>
            {
                new()
                {
                    Name = "5-hour quota",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Burst,
                    NextResetTime = burstReset,
                    // PercentageValue intentionally null — simulates post-pipeline state before the fix
                },
                new()
                {
                    Name = "Weekly quota",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Rolling,
                    NextResetTime = weeklyReset,
                    // PercentageValue intentionally null
                },
            },
        };

        var resetTimes = ProviderResetBadgePresentationCatalog.ResolveResetTimes(usage, suppressSingleResetFallback: false);

        // TryGetPresentation fails → falls back to single NextResetTime
        var onlyReset = Assert.Single(resetTimes);
        Assert.Equal(burstReset, onlyReset);
        Assert.DoesNotContain(weeklyReset, resetTimes);
    }
}
