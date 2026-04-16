// <copyright file="PaceCalculationEndToEndTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using Xunit;

namespace AIUsageTracker.Tests.Integration;

/// <summary>
/// End-to-end pace calculation tests that exercise the FULL pipeline from raw ProviderUsage
/// data through UsageMath projection to pace badge determination. Uses real classes throughout.
/// </summary>
public class PaceCalculationEndToEndTests
{
    /// <summary>
    /// Codex at 71% used with weekly reset in 5 days.
    /// Elapsed = 2/7 days (~28.6%), projected = 71/0.286 = ~248% clamped to 100%.
    /// Badge: "Over pace".
    /// </summary>
    [Fact]
    public void Codex_71PercentUsed_WeeklyResetIn5Days_ShowsOverPace()
    {
        // Arrange — 7-day rolling window, 2 days elapsed, 5 days remaining
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var nextResetUtc = now.AddDays(5);
        var periodDuration = TimeSpan.FromDays(7);

        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "OpenAI (Codex)",
            UsedPercent = 71,
            RequestsUsed = 71,
            RequestsAvailable = 100,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            IsAvailable = true,
            NextResetTime = nextResetUtc.ToLocalTime(),
            PeriodDuration = periodDuration,
        };

        // Act — use the unified ComputePaceColor API
        var paceColor = UsageMath.ComputePaceColor(
            usage.UsedPercent,
            usage.NextResetTime,
            usage.PeriodDuration,
            nowUtc: now);

        // Assert — 71% used in 2/7 elapsed → heavily over pace
        Assert.True(paceColor.IsPaceAdjusted);
        Assert.Equal(PaceTier.OverPace, paceColor.PaceTier);
        Assert.Equal(100.0, paceColor.ProjectedPercent); // clamped
        Assert.Equal("Over pace", paceColor.BadgeText);
    }

    /// <summary>
    /// Codex at 20% used with weekly reset in 5 days.
    /// Elapsed = 2/7 days (~28.6%), projected = 20/0.286 = ~70%.
    /// Badge: "On pace".
    /// </summary>
    [Fact]
    public void Codex_20PercentUsed_WeeklyResetIn5Days_ShowsOnPace()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var nextResetUtc = now.AddDays(5);
        var periodDuration = TimeSpan.FromDays(7);

        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "OpenAI (Codex)",
            UsedPercent = 20,
            RequestsUsed = 20,
            RequestsAvailable = 100,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            IsAvailable = true,
            NextResetTime = nextResetUtc.ToLocalTime(),
            PeriodDuration = periodDuration,
        };

        var paceColor = UsageMath.ComputePaceColor(
            usage.UsedPercent, usage.NextResetTime, usage.PeriodDuration, nowUtc: now);

        // 20% used in ~28.6% elapsed → projected ~70%
        Assert.True(paceColor.ProjectedPercent < 80, $"Projected {paceColor.ProjectedPercent:F1}% should be < 80%");
        Assert.True(paceColor.ProjectedPercent > 50, $"Projected {paceColor.ProjectedPercent:F1}% should be > 50%");
        Assert.Equal("On pace", paceColor.BadgeText);
    }

    /// <summary>
    /// 5-hour burst window at 60% used after 2 hours.
    /// Elapsed fraction = 2/5 = 0.4, projected = 60/0.4 = 150%, clamped to 100%.
    /// Badge: "Over pace".
    /// </summary>
    [Fact]
    public void BurstWindow_60PercentUsed_After2Hours_ShowsOverPace()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var periodDuration = TimeSpan.FromHours(5);
        var nextResetUtc = now.AddHours(3); // 2 hours elapsed, 3 remaining

        var usage = new ProviderUsage
        {
            ProviderId = "codex.burst",
            ProviderName = "Burst Window",
            UsedPercent = 60,
            RequestsUsed = 60,
            RequestsAvailable = 100,
            IsQuotaBased = true,
            IsAvailable = true,
            NextResetTime = nextResetUtc.ToLocalTime(),
            PeriodDuration = periodDuration,
        };

        var paceColor = UsageMath.ComputePaceColor(
            usage.UsedPercent, usage.NextResetTime, usage.PeriodDuration, nowUtc: now);

        // 60% / 0.4 = 150% clamped to 100%
        Assert.Equal(100.0, paceColor.ProjectedPercent);
        Assert.Equal("Over pace", paceColor.BadgeText);
    }

    /// <summary>
    /// 5-hour burst window at 10% used after 2 hours.
    /// Elapsed fraction = 2/5 = 0.4, projected = 10/0.4 = 25%.
    /// Badge: "On pace".
    /// </summary>
    [Fact]
    public void BurstWindow_10PercentUsed_After2Hours_ShowsOnPace()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var periodDuration = TimeSpan.FromHours(5);
        var nextResetUtc = now.AddHours(3);

        var usage = new ProviderUsage
        {
            ProviderId = "codex.burst",
            ProviderName = "Burst Window",
            UsedPercent = 10,
            RequestsUsed = 10,
            RequestsAvailable = 100,
            IsQuotaBased = true,
            IsAvailable = true,
            NextResetTime = nextResetUtc.ToLocalTime(),
            PeriodDuration = periodDuration,
        };

        var paceColor = UsageMath.ComputePaceColor(
            usage.UsedPercent, usage.NextResetTime, usage.PeriodDuration, nowUtc: now);

        // 10% / 0.4 = 25%
        Assert.True(
            paceColor.ProjectedPercent >= 24 && paceColor.ProjectedPercent <= 26,
            $"Projected {paceColor.ProjectedPercent:F1}% should be ~25%");
        Assert.Equal("Headroom", paceColor.BadgeText);
    }

    /// <summary>
    /// Verifies that ComputePaceColor ColorPercent equals raw usedPercent (no scaling),
    /// and that tier and badge agree.
    /// </summary>
    [Fact]
    public void ComputePaceColor_ColorPercent_IsRawUsedPercent()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var nextResetUtc = now.AddDays(3);
        var period = TimeSpan.FromDays(7);

        var result = UsageMath.ComputePaceColor(45, nextResetUtc, period, nowUtc: now);

        Assert.Equal(45.0, result.ColorPercent, precision: 6);
        Assert.True(result.IsPaceAdjusted);
        Assert.NotEmpty(result.BadgeText);
    }

    /// <summary>
    /// Edge case: period fully elapsed (nextReset == now). Elapsed fraction clamped to 1.0,
    /// so projected equals raw used percent.
    /// </summary>
    [Fact]
    public void FullyElapsedPeriod_ProjectedEqualsRawPercent()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var nextResetUtc = now; // fully elapsed
        var period = TimeSpan.FromDays(7);

        var result = UsageMath.ComputePaceColor(50, nextResetUtc, period, nowUtc: now);

        Assert.Equal(50.0, result.ProjectedPercent, precision: 1);
    }

    [Theory]
    [InlineData(0, PaceTier.Headroom, "Headroom")]
    [InlineData(30, PaceTier.Headroom, "Headroom")]
    [InlineData(69.9, PaceTier.Headroom, "Headroom")]
    [InlineData(70.0, PaceTier.OnPace, "On pace")]
    [InlineData(85, PaceTier.OnPace, "On pace")]
    [InlineData(99.9, PaceTier.OnPace, "On pace")]
    [InlineData(100.0, PaceTier.OverPace, "Over pace")]
    [InlineData(150, PaceTier.OverPace, "Over pace")]
    public void ClassifyPace_ReturnsCorrectTierAndText(double projected, PaceTier expectedTier, string expectedText)
    {
        var result = UsageMath.ClassifyPace(projected);

        Assert.Equal(expectedTier, result.Tier);
        Assert.Equal(expectedText, result.Text);
        Assert.Equal(projected, result.ProjectedPercent);
        Assert.Contains("%", result.ProjectedText, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputePaceColor_WhenDisabled_NotPaceAdjusted()
    {
        var result = UsageMath.ComputePaceColor(
            50.0,
            nextResetTime: DateTime.UtcNow.AddDays(3),
            periodDuration: TimeSpan.FromDays(7),
            enablePaceAdjustment: false);

        Assert.False(result.IsPaceAdjusted);
        Assert.Equal(string.Empty, result.BadgeText);
    }

    [Fact]
    public void ComputePaceColor_WhenEnabled_ReturnsBadgeText()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var result = UsageMath.ComputePaceColor(
            20.0,
            nextResetTime: now.AddDays(5),
            periodDuration: TimeSpan.FromDays(7),
            nowUtc: now);

        Assert.True(result.IsPaceAdjusted);
        Assert.Equal(PaceTier.OnPace, result.PaceTier);
        Assert.NotEmpty(result.ProjectedText);
    }
}
