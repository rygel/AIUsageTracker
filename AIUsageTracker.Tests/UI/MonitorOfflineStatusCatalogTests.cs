// <copyright file="MonitorOfflineStatusCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class MonitorOfflineStatusCatalogTests
{
    [Fact]
    public void Format_NoLastUpdate_ReturnsNoDataMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 0, 0);

        var status = MonitorOfflineStatusCatalog.Format(DateTime.MinValue, now);

        Assert.Equal("Monitor offline — no data received yet", status);
    }

    [Fact]
    public void Format_SecondsElapsed_ReturnsSecondsMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 0, 30);
        var lastUpdate = now.AddSeconds(-12);

        var status = MonitorOfflineStatusCatalog.Format(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 12s ago", status);
    }

    [Fact]
    public void Format_MinutesElapsed_ReturnsMinutesMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 45, 0);
        var lastUpdate = now.AddMinutes(-17);

        var status = MonitorOfflineStatusCatalog.Format(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 17m ago", status);
    }

    [Fact]
    public void Format_HoursElapsed_ReturnsHoursMessage()
    {
        var now = new DateTime(2026, 3, 21, 19, 0, 0);
        var lastUpdate = now.AddHours(-3).AddMinutes(-20);

        var status = MonitorOfflineStatusCatalog.Format(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 3h ago", status);
    }
}
