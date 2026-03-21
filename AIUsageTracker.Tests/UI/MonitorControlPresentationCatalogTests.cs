// <copyright file="MonitorControlPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class MonitorControlPresentationCatalogTests
{
    [Fact]
    public void CreateRestarting_ReturnsWarningWithoutSideEffects()
    {
        var presentation = MonitorControlPresentationCatalog.CreateRestarting();

        Assert.Equal("Restarting monitor...", presentation.Message);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
        Assert.False(presentation.UpdateToggleButton);
        Assert.False(presentation.TriggerRefreshData);
    }

    [Theory]
    [InlineData(true, "Monitor restarted", StatusType.Success, true)]
    [InlineData(false, "Monitor restart failed", StatusType.Error, false)]
    public void CreateRestartResult_MapsMonitorReadiness(
        bool monitorReady,
        string expectedMessage,
        StatusType expectedStatus,
        bool expectedRefresh)
    {
        var presentation = MonitorControlPresentationCatalog.CreateRestartResult(monitorReady);

        Assert.Equal(expectedMessage, presentation.Message);
        Assert.Equal(expectedStatus, presentation.StatusType);
        Assert.False(presentation.UpdateToggleButton);
        Assert.Equal(expectedRefresh, presentation.TriggerRefreshData);
    }

    [Fact]
    public void CreateRestartError_FormatsMessage()
    {
        var presentation = MonitorControlPresentationCatalog.CreateRestartError("boom");

        Assert.Equal("Restart error: boom", presentation.Message);
        Assert.Equal(StatusType.Error, presentation.StatusType);
        Assert.False(presentation.UpdateToggleButton);
        Assert.False(presentation.TriggerRefreshData);
    }

    [Fact]
    public void CreateStopping_ReturnsWarningWithoutSideEffects()
    {
        var presentation = MonitorControlPresentationCatalog.CreateStopping();

        Assert.Equal("Stopping monitor...", presentation.Message);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
        Assert.False(presentation.UpdateToggleButton);
        Assert.False(presentation.TriggerRefreshData);
    }

    [Theory]
    [InlineData(true, "Monitor stopped", StatusType.Info, true, false)]
    [InlineData(false, "Failed to stop monitor", StatusType.Error, false, false)]
    public void CreateStopResult_MapsStopOutcome(
        bool stopped,
        string expectedMessage,
        StatusType expectedStatus,
        bool expectedUpdateToggle,
        bool expectedRunningState)
    {
        var presentation = MonitorControlPresentationCatalog.CreateStopResult(stopped);

        Assert.Equal(expectedMessage, presentation.Message);
        Assert.Equal(expectedStatus, presentation.StatusType);
        Assert.Equal(expectedUpdateToggle, presentation.UpdateToggleButton);
        Assert.Equal(expectedRunningState, presentation.ToggleRunningState);
        Assert.False(presentation.TriggerRefreshData);
    }

    [Fact]
    public void CreateStarting_ReturnsWarningWithoutSideEffects()
    {
        var presentation = MonitorControlPresentationCatalog.CreateStarting();

        Assert.Equal("Starting monitor...", presentation.Message);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
        Assert.False(presentation.UpdateToggleButton);
        Assert.False(presentation.TriggerRefreshData);
    }

    [Theory]
    [InlineData(true, "Monitor started", StatusType.Success, true, true, true)]
    [InlineData(false, "Monitor failed to start", StatusType.Error, true, false, false)]
    public void CreateStartResult_MapsStartOutcome(
        bool monitorReady,
        string expectedMessage,
        StatusType expectedStatus,
        bool expectedUpdateToggle,
        bool expectedRunningState,
        bool expectedRefresh)
    {
        var presentation = MonitorControlPresentationCatalog.CreateStartResult(monitorReady);

        Assert.Equal(expectedMessage, presentation.Message);
        Assert.Equal(expectedStatus, presentation.StatusType);
        Assert.Equal(expectedUpdateToggle, presentation.UpdateToggleButton);
        Assert.Equal(expectedRunningState, presentation.ToggleRunningState);
        Assert.Equal(expectedRefresh, presentation.TriggerRefreshData);
    }
}
