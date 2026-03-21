// <copyright file="StatusPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Tests.UI;

public sealed class StatusPresentationCatalogTests
{
    [Fact]
    public void Create_SuccessWithContractWarning_EscalatesToWarning()
    {
        var presentation = StatusPresentationCatalog.Create(
            message: "Connected",
            type: StatusType.Success,
            monitorContractWarningMessage: "Contract mismatch",
            lastMonitorUpdate: DateTime.MinValue);

        Assert.Equal("Contract mismatch", presentation.Message);
        Assert.Equal(StatusType.Warning, presentation.Type);
        Assert.Equal(StatusIndicatorKind.Warning, presentation.IndicatorKind);
        Assert.Equal(LogLevel.Warning, presentation.LogLevel);
    }

    [Fact]
    public void Create_NoUpdateTimestamp_UsesNeverTooltip()
    {
        var presentation = StatusPresentationCatalog.Create(
            message: "Loading",
            type: StatusType.Info,
            monitorContractWarningMessage: null,
            lastMonitorUpdate: DateTime.MinValue);

        Assert.Equal("Last update: Never", presentation.TooltipText);
    }

    [Fact]
    public void Create_WithUpdateTimestamp_FormatsTooltipTime()
    {
        var presentation = StatusPresentationCatalog.Create(
            message: "Connected",
            type: StatusType.Success,
            monitorContractWarningMessage: null,
            lastMonitorUpdate: new DateTime(2026, 3, 21, 18, 45, 12));

        Assert.Equal("Last update: 18:45:12", presentation.TooltipText);
    }

    [Theory]
    [InlineData(StatusType.Info, 0, LogLevel.Information)]
    [InlineData(StatusType.Success, 1, LogLevel.Information)]
    [InlineData(StatusType.Warning, 2, LogLevel.Warning)]
    [InlineData(StatusType.Error, 3, LogLevel.Error)]
    public void Create_MapsTypeToIndicatorAndLogLevel(
        StatusType type,
        int expectedIndicator,
        LogLevel expectedLogLevel)
    {
        var presentation = StatusPresentationCatalog.Create(
            message: "Status",
            type: type,
            monitorContractWarningMessage: null,
            lastMonitorUpdate: DateTime.MinValue);

        Assert.Equal(expectedIndicator, (int)presentation.IndicatorKind);
        Assert.Equal(expectedLogLevel, presentation.LogLevel);
    }
}
