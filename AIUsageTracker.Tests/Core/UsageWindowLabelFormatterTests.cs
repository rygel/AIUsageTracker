// <copyright file="UsageWindowLabelFormatterTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Utilities;

namespace AIUsageTracker.Tests.Core;

public sealed class UsageWindowLabelFormatterTests
{
    [Theory]
    [InlineData(60, "TIME_UNIT_MINUTE", "Hourly")]
    [InlineData(180, "TIME_UNIT_MINUTE", "3h")]
    [InlineData(300, "TIME_UNIT_MINUTE", "5h")]
    [InlineData(5, "TIME_UNIT_HOUR", "5h")]
    [InlineData(1, "TIME_UNIT_HOUR", "Hourly")]
    [InlineData(7, "TIME_UNIT_DAY", "7d")]
    public void FormatDuration_ReturnsNormalizedLabel(long duration, string unit, string expected)
    {
        var result = UsageWindowLabelFormatter.FormatDuration(duration, unit);

        Assert.Equal(expected, result);
    }
}
