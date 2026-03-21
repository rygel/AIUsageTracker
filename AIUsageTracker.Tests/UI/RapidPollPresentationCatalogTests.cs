// <copyright file="RapidPollPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class RapidPollPresentationCatalogTests
{
    [Fact]
    public void CreateInitialLoading_ReturnsInfoLoadingMessage()
    {
        var presentation = RapidPollPresentationCatalog.CreateInitialLoading();

        Assert.Equal("Loading data...", presentation.StatusMessage);
        Assert.Equal(StatusType.Info, presentation.StatusType);
        Assert.Null(presentation.ErrorStateMessage);
    }

    [Fact]
    public void CreateMonitorNotReachable_UsesProvidedErrorMessage()
    {
        var presentation = RapidPollPresentationCatalog.CreateMonitorNotReachable("connection details");

        Assert.Equal("Monitor not reachable", presentation.StatusMessage);
        Assert.Equal(StatusType.Error, presentation.StatusType);
        Assert.Equal("connection details", presentation.ErrorStateMessage);
    }

    [Fact]
    public void CreateScanningForProviders_ReturnsInfoStatus()
    {
        var presentation = RapidPollPresentationCatalog.CreateScanningForProviders();

        Assert.Equal("Scanning for providers...", presentation.StatusMessage);
        Assert.Equal(StatusType.Info, presentation.StatusType);
    }

    [Fact]
    public void CreateWaitingForData_FormatsAttemptCounter()
    {
        var presentation = RapidPollPresentationCatalog.CreateWaitingForData(attempt: 2, maxAttempts: 15);

        Assert.Equal("Waiting for data... (3/15)", presentation.StatusMessage);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
    }

    [Fact]
    public void CreateConnectionLost_IncludesExceptionMessageInErrorState()
    {
        var presentation = RapidPollPresentationCatalog.CreateConnectionLost("timeout");

        Assert.Equal("Connection lost", presentation.StatusMessage);
        Assert.Equal(StatusType.Error, presentation.StatusType);
        Assert.Equal("Lost connection to Monitor:\ntimeout\n\nTry refreshing or restarting the Monitor.", presentation.ErrorStateMessage);
    }

    [Fact]
    public void CreateNoDataAfterMaxAttempts_UsesExpectedMessages()
    {
        var presentation = RapidPollPresentationCatalog.CreateNoDataAfterMaxAttempts();

        Assert.Equal("No data available", presentation.StatusMessage);
        Assert.Equal(StatusType.Error, presentation.StatusType);
        Assert.Equal(
            "No provider data available.\n\nThe Monitor may still be initializing.\nTry refreshing manually or check Settings > Monitor.",
            presentation.ErrorStateMessage);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ShouldTriggerBackgroundRefresh_OnlyOnFirstAttempt(int attempt, bool expected)
    {
        var shouldTrigger = RapidPollPresentationCatalog.ShouldTriggerBackgroundRefresh(attempt);

        Assert.Equal(expected, shouldTrigger);
    }

    [Theory]
    [InlineData(0, 15, true)]
    [InlineData(13, 15, true)]
    [InlineData(14, 15, false)]
    public void ShouldWaitBeforeRetry_StopsOnLastAttempt(int attempt, int maxAttempts, bool expected)
    {
        var shouldWait = RapidPollPresentationCatalog.ShouldWaitBeforeRetry(attempt, maxAttempts);

        Assert.Equal(expected, shouldWait);
    }
}
