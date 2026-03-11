// <copyright file="MonitorApiRoutesTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Tests.Core;

public class MonitorApiRoutesTests
{
    [Fact]
    public void UsageByProvider_WithSpecialCharacters_EscapesPathSegment()
    {
        var route = MonitorApiRoutes.UsageByProvider("open ai/x");

        Assert.Equal("/api/usage/open%20ai%2Fx", route);
    }

    [Fact]
    public void ConfigByProvider_WithSpecialCharacters_EscapesPathSegment()
    {
        var route = MonitorApiRoutes.ConfigByProvider("github/copilot");

        Assert.Equal("/api/config/github%2Fcopilot", route);
    }

    [Fact]
    public void HistoryWithLimit_UsesInvariantFormatting()
    {
        var route = MonitorApiRoutes.HistoryWithLimit(100);

        Assert.Equal("/api/history?limit=100", route);
    }

    [Fact]
    public void ExportWithWindow_FormatsQueryString()
    {
        var route = MonitorApiRoutes.ExportWithWindow("json+pretty", 30);

        Assert.Equal("/api/export?format=json%2Bpretty&days=30", route);
    }
}
