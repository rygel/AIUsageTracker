// <copyright file="RefreshDataPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class RefreshDataPresentationCatalogTests
{
    [Fact]
    public void Create_WithLatestUsages_ReturnsSuccessPresentation()
    {
        var now = new DateTime(2026, 3, 21, 14, 30, 0);
        var presentation = RefreshDataPresentationCatalog.Create(
            hasLatestUsages: true,
            hasCurrentUsages: false,
            now: now);

        Assert.True(presentation.ApplyLatestUsages);
        Assert.True(presentation.UpdateLastMonitorTimestamp);
        Assert.Equal("14:30:00", presentation.StatusMessage);
        Assert.Equal(StatusType.Success, presentation.StatusType);
        Assert.True(presentation.TriggerTrayIconUpdate);
        Assert.False(presentation.UseErrorState);
        Assert.Null(presentation.ErrorStateMessage);
    }

    [Fact]
    public void Create_WithoutLatestButWithCurrentUsages_ReturnsWarningPresentation()
    {
        var presentation = RefreshDataPresentationCatalog.Create(
            hasLatestUsages: false,
            hasCurrentUsages: true,
            now: new DateTime(2026, 3, 21, 14, 30, 0));

        Assert.False(presentation.ApplyLatestUsages);
        Assert.False(presentation.UpdateLastMonitorTimestamp);
        Assert.Equal("Refresh returned no data, keeping last snapshot", presentation.StatusMessage);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
        Assert.False(presentation.TriggerTrayIconUpdate);
        Assert.False(presentation.UseErrorState);
        Assert.Null(presentation.ErrorStateMessage);
    }

    [Fact]
    public void Create_WithoutAnyUsages_ReturnsErrorStatePresentation()
    {
        var presentation = RefreshDataPresentationCatalog.Create(
            hasLatestUsages: false,
            hasCurrentUsages: false,
            now: new DateTime(2026, 3, 21, 14, 30, 0));

        Assert.False(presentation.ApplyLatestUsages);
        Assert.False(presentation.UpdateLastMonitorTimestamp);
        Assert.Null(presentation.StatusMessage);
        Assert.Null(presentation.StatusType);
        Assert.False(presentation.TriggerTrayIconUpdate);
        Assert.True(presentation.UseErrorState);
        Assert.Equal("No provider data available.\n\nMonitor may still be initializing.", presentation.ErrorStateMessage);
    }
}
