// <copyright file="MainWindowRuntimeLogicTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class MainWindowRuntimeLogicTests
{
    [Fact]
    public void CreatePollingRefreshDecision_BelowCooldown_DoesNotTriggerRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = MainWindowRuntimeLogic.CreatePollingRefreshDecision(
            lastRefreshTrigger: now.AddSeconds(-30),
            now: now,
            refreshCooldownSeconds: 120);

        Assert.False(decision.ShouldTriggerRefresh);
        Assert.Equal(30, decision.SecondsSinceLastRefresh);
    }

    [Fact]
    public void CreatePollingRefreshDecision_AtCooldown_TriggersRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = MainWindowRuntimeLogic.CreatePollingRefreshDecision(
            lastRefreshTrigger: now.AddSeconds(-120),
            now: now,
            refreshCooldownSeconds: 120);

        Assert.True(decision.ShouldTriggerRefresh);
        Assert.Equal(120, decision.SecondsSinceLastRefresh);
    }

    [Fact]
    public void CreatePollingRefreshDecision_NeverRefreshed_TriggersRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = MainWindowRuntimeLogic.CreatePollingRefreshDecision(
            lastRefreshTrigger: DateTime.MinValue,
            now: now,
            refreshCooldownSeconds: 120);

        Assert.True(decision.ShouldTriggerRefresh);
        Assert.True(decision.SecondsSinceLastRefresh > 120);
    }

    [Fact]
    public void ShouldRefreshTrayConfigs_WithoutCachedConfigs_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = MainWindowRuntimeLogic.ShouldRefreshTrayConfigs(
            hasCachedConfigs: false,
            lastRefreshUtc: now.AddMinutes(-1),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshTrayConfigs_WithFreshCachedConfigs_ReturnsFalse()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = MainWindowRuntimeLogic.ShouldRefreshTrayConfigs(
            hasCachedConfigs: true,
            lastRefreshUtc: now.AddMinutes(-2),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshTrayConfigs_WithStaleCachedConfigs_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = MainWindowRuntimeLogic.ShouldRefreshTrayConfigs(
            hasCachedConfigs: true,
            lastRefreshUtc: now.AddMinutes(-10),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void FormatMonitorOfflineStatus_NoLastUpdate_ReturnsNoDataMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 0, 0);

        var status = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(DateTime.MinValue, now);

        Assert.Equal("Monitor offline — no data received yet", status);
    }

    [Fact]
    public void FormatMonitorOfflineStatus_SecondsElapsed_ReturnsSecondsMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 0, 30);
        var lastUpdate = now.AddSeconds(-12);

        var status = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 12s ago", status);
    }

    [Fact]
    public void FormatMonitorOfflineStatus_MinutesElapsed_ReturnsMinutesMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 45, 0);
        var lastUpdate = now.AddMinutes(-17);

        var status = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 17m ago", status);
    }

    [Fact]
    public void FormatMonitorOfflineStatus_HoursElapsed_ReturnsHoursMessage()
    {
        var now = new DateTime(2026, 3, 21, 19, 0, 0);
        var lastUpdate = now.AddHours(-3).AddMinutes(-20);

        var status = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 3h ago", status);
    }
}
