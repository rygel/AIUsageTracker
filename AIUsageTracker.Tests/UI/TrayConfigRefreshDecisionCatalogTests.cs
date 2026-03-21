// <copyright file="TrayConfigRefreshDecisionCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class TrayConfigRefreshDecisionCatalogTests
{
    [Fact]
    public void ShouldRefresh_WithoutCachedConfigs_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = TrayConfigRefreshDecisionCatalog.ShouldRefresh(
            hasCachedConfigs: false,
            lastRefreshUtc: now.AddMinutes(-1),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefresh_WithFreshCachedConfigs_ReturnsFalse()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = TrayConfigRefreshDecisionCatalog.ShouldRefresh(
            hasCachedConfigs: true,
            lastRefreshUtc: now.AddMinutes(-2),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefresh_WithStaleCachedConfigs_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = TrayConfigRefreshDecisionCatalog.ShouldRefresh(
            hasCachedConfigs: true,
            lastRefreshUtc: now.AddMinutes(-10),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.True(shouldRefresh);
    }
}
