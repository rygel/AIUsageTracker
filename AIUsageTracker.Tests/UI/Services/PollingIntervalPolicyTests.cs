// <copyright file="PollingIntervalPolicyTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim.Services;

namespace AIUsageTracker.Tests.UI.Services;

public class PollingIntervalPolicyTests
{
    private readonly PollingIntervalPolicy _policy = new();

    [Fact]
    public void DefaultInterval_IsOneMinute()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), this._policy.DefaultInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Normalize_WhenIntervalIsNonPositive_ReturnsDefaultInterval(int seconds)
    {
        var normalized = this._policy.Normalize(TimeSpan.FromSeconds(seconds));

        Assert.Equal(this._policy.DefaultInterval, normalized);
    }

    [Fact]
    public void Normalize_WhenIntervalIsBelowMinimum_ClampsToMinimum()
    {
        var normalized = this._policy.Normalize(TimeSpan.FromMilliseconds(500));

        Assert.Equal(TimeSpan.FromSeconds(2), normalized);
    }

    [Fact]
    public void Normalize_WhenIntervalIsAboveMaximum_ClampsToMaximum()
    {
        var normalized = this._policy.Normalize(TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromMinutes(30), normalized);
    }

    [Fact]
    public void Normalize_WhenIntervalIsWithinRange_ReturnsUnchanged()
    {
        var requested = TimeSpan.FromSeconds(30);
        var normalized = this._policy.Normalize(requested);

        Assert.Equal(requested, normalized);
    }
}
