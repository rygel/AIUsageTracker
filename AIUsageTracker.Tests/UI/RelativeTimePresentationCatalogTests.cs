// <copyright file="RelativeTimePresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class RelativeTimePresentationCatalogTests
{
    private static readonly DateTime Now = new(2026, 3, 21, 12, 0, 0, DateTimeKind.Local);

    [Fact]
    public void FormatUntil_PastTime_ReturnsZeroMinutes()
    {
        var result = RelativeTimePresentationCatalog.FormatUntil(Now.AddSeconds(-1), Now);

        Assert.Equal("0m", result);
    }

    [Fact]
    public void FormatUntil_DaysAndHours_ReturnsDayHourFormat()
    {
        var result = RelativeTimePresentationCatalog.FormatUntil(Now.AddDays(2).AddHours(3).AddMinutes(40), Now);

        Assert.Equal("2d 3h", result);
    }

    [Fact]
    public void FormatUntil_HoursAndMinutes_ReturnsHourMinuteFormat()
    {
        var result = RelativeTimePresentationCatalog.FormatUntil(Now.AddHours(4).AddMinutes(15), Now);

        Assert.Equal("4h 15m", result);
    }

    [Fact]
    public void FormatUntil_SubMinute_RoundsUpToOneMinute()
    {
        var result = RelativeTimePresentationCatalog.FormatUntil(Now.AddSeconds(10), Now);

        Assert.Equal("1m", result);
    }
}
