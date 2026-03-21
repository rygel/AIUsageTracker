// <copyright file="MonitorContractPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class MonitorContractPresentationCatalogTests
{
    [Fact]
    public void Create_WhenCompatible_ClearsWarningWithoutStatus()
    {
        var presentation = MonitorContractPresentationCatalog.Create(
            isCompatible: true,
            message: "unused");

        Assert.Null(presentation.WarningMessage);
        Assert.False(presentation.ShowStatus);
        Assert.Null(presentation.StatusMessage);
        Assert.Null(presentation.StatusType);
    }

    [Fact]
    public void Create_WhenIncompatible_SetsWarningAndWarningStatus()
    {
        var presentation = MonitorContractPresentationCatalog.Create(
            isCompatible: false,
            message: "Monitor version mismatch");

        Assert.Equal("Monitor version mismatch", presentation.WarningMessage);
        Assert.True(presentation.ShowStatus);
        Assert.Equal("Monitor version mismatch", presentation.StatusMessage);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
    }
}
