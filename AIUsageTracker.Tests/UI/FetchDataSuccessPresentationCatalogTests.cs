// <copyright file="FetchDataSuccessPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class FetchDataSuccessPresentationCatalogTests
{
    [Fact]
    public void Create_WithNonNormalInterval_SwitchesToNormal()
    {
        var now = new DateTime(2026, 3, 21, 11, 5, 0);
        var presentation = FetchDataSuccessPresentationCatalog.Create(
            now: now,
            statusSuffix: " (real-time)",
            hasPollingTimer: true,
            currentInterval: TimeSpan.FromSeconds(5),
            normalInterval: TimeSpan.FromMinutes(1));

        Assert.Equal("11:05:00 (real-time)", presentation.StatusMessage);
        Assert.True(presentation.SwitchToNormalInterval);
    }

    [Fact]
    public void Create_WithNormalInterval_DoesNotSwitch()
    {
        var now = new DateTime(2026, 3, 21, 11, 6, 0);
        var presentation = FetchDataSuccessPresentationCatalog.Create(
            now: now,
            statusSuffix: string.Empty,
            hasPollingTimer: true,
            currentInterval: TimeSpan.FromMinutes(1),
            normalInterval: TimeSpan.FromMinutes(1));

        Assert.Equal("11:06:00", presentation.StatusMessage);
        Assert.False(presentation.SwitchToNormalInterval);
    }

    [Fact]
    public void Create_WithoutPollingTimer_DoesNotSwitch()
    {
        var now = new DateTime(2026, 3, 21, 11, 7, 0);
        var presentation = FetchDataSuccessPresentationCatalog.Create(
            now: now,
            statusSuffix: string.Empty,
            hasPollingTimer: false,
            currentInterval: TimeSpan.FromSeconds(5),
            normalInterval: TimeSpan.FromMinutes(1));

        Assert.Equal("11:07:00", presentation.StatusMessage);
        Assert.False(presentation.SwitchToNormalInterval);
    }
}
