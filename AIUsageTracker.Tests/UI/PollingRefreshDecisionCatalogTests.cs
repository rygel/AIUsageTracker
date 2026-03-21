// <copyright file="PollingRefreshDecisionCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class PollingRefreshDecisionCatalogTests
{
    [Fact]
    public void Create_BelowCooldown_DoesNotTriggerRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = PollingRefreshDecisionCatalog.Create(
            lastRefreshTrigger: now.AddSeconds(-30),
            now: now,
            refreshCooldownSeconds: 120);

        Assert.False(decision.ShouldTriggerRefresh);
        Assert.Equal(30, decision.SecondsSinceLastRefresh);
    }

    [Fact]
    public void Create_AtCooldown_TriggersRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = PollingRefreshDecisionCatalog.Create(
            lastRefreshTrigger: now.AddSeconds(-120),
            now: now,
            refreshCooldownSeconds: 120);

        Assert.True(decision.ShouldTriggerRefresh);
        Assert.Equal(120, decision.SecondsSinceLastRefresh);
    }

    [Fact]
    public void Create_NeverRefreshed_TriggersRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = PollingRefreshDecisionCatalog.Create(
            lastRefreshTrigger: DateTime.MinValue,
            now: now,
            refreshCooldownSeconds: 120);

        Assert.True(decision.ShouldTriggerRefresh);
        Assert.True(decision.SecondsSinceLastRefresh > 120);
    }
}
