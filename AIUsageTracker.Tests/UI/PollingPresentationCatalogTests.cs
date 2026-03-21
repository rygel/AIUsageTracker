// <copyright file="PollingPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class PollingPresentationCatalogTests
{
    [Fact]
    public void ResolveInitialInterval_UsesNormalWhenUsagesPresent()
    {
        var startup = TimeSpan.FromSeconds(2);
        var normal = TimeSpan.FromMinutes(1);

        var interval = PollingPresentationCatalog.ResolveInitialInterval(
            hasUsages: true,
            startupInterval: startup,
            normalInterval: normal);

        Assert.Equal(normal, interval);
    }

    [Fact]
    public void ResolveInitialInterval_UsesStartupWhenNoUsages()
    {
        var startup = TimeSpan.FromSeconds(2);
        var normal = TimeSpan.FromMinutes(1);

        var interval = PollingPresentationCatalog.ResolveInitialInterval(
            hasUsages: false,
            startupInterval: startup,
            normalInterval: normal);

        Assert.Equal(startup, interval);
    }

    [Fact]
    public void ResolveAfterEmptyRetry_NoUsages_ReturnsWarningAndStartupSwitch()
    {
        var presentation = PollingPresentationCatalog.ResolveAfterEmptyRetry(
            hasCurrentUsages: false,
            lastMonitorUpdate: DateTime.MinValue,
            now: new DateTime(2026, 3, 21, 12, 0, 0));

        Assert.Equal("No data - waiting for Monitor", presentation.Message);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
        Assert.True(presentation.SwitchToStartupInterval);
    }

    [Fact]
    public void ResolveAfterEmptyRetry_StaleUsages_ReturnsOfflineWarning()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var presentation = PollingPresentationCatalog.ResolveAfterEmptyRetry(
            hasCurrentUsages: true,
            lastMonitorUpdate: now.AddMinutes(-7),
            now: now);

        Assert.Equal("Monitor offline — last sync 7m ago", presentation.Message);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
        Assert.False(presentation.SwitchToStartupInterval);
    }

    [Fact]
    public void ResolveAfterEmptyRetry_RecentUsages_ReturnsNoStatusChange()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var presentation = PollingPresentationCatalog.ResolveAfterEmptyRetry(
            hasCurrentUsages: true,
            lastMonitorUpdate: now.AddMinutes(-1),
            now: now);

        Assert.Null(presentation.Message);
        Assert.Null(presentation.StatusType);
        Assert.False(presentation.SwitchToStartupInterval);
    }

    [Fact]
    public void ResolveOnPollingException_WithOldData_ReturnsOfflineWarning()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var presentation = PollingPresentationCatalog.ResolveOnPollingException(
            hasOldData: true,
            lastMonitorUpdate: now.AddMinutes(-9),
            now: now);

        Assert.Equal("Monitor offline — last sync 9m ago", presentation.Message);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
        Assert.False(presentation.SwitchToStartupInterval);
    }

    [Fact]
    public void ResolveOnPollingException_WithoutOldData_ReturnsConnectionErrorAndStartupSwitch()
    {
        var presentation = PollingPresentationCatalog.ResolveOnPollingException(
            hasOldData: false,
            lastMonitorUpdate: DateTime.MinValue,
            now: new DateTime(2026, 3, 21, 12, 0, 0));

        Assert.Equal("Connection error", presentation.Message);
        Assert.Equal(StatusType.Error, presentation.StatusType);
        Assert.True(presentation.SwitchToStartupInterval);
    }
}
