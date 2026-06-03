// <copyright file="MainWindowRuntimeLogicTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class MainWindowRuntimeLogicTests
{
    [Fact]
    public void CreatePollingRefreshDecision_BelowCooldown_DoesNotTriggerRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = MainWindowRuntimeLogic.CreatePollingRefreshDecision(
            lastRefreshTrigger: now.AddSeconds(-30),
            now: now,
            refreshCooldownSeconds: 120);

        Assert.False(decision.ShouldTriggerRefresh);
        Assert.Equal(30, decision.SecondsSinceLastRefresh);
    }

    [Fact]
    public void CreatePollingRefreshDecision_AtCooldown_TriggersRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = MainWindowRuntimeLogic.CreatePollingRefreshDecision(
            lastRefreshTrigger: now.AddSeconds(-120),
            now: now,
            refreshCooldownSeconds: 120);

        Assert.True(decision.ShouldTriggerRefresh);
        Assert.Equal(120, decision.SecondsSinceLastRefresh);
    }

    [Fact]
    public void CreatePollingRefreshDecision_NeverRefreshed_TriggersRefresh()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0);
        var decision = MainWindowRuntimeLogic.CreatePollingRefreshDecision(
            lastRefreshTrigger: DateTime.MinValue,
            now: now,
            refreshCooldownSeconds: 120);

        Assert.True(decision.ShouldTriggerRefresh);
        Assert.True(decision.SecondsSinceLastRefresh > 120);
    }

    [Fact]
    public void ShouldRefreshTrayConfigs_WithoutCachedConfigs_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = MainWindowRuntimeLogic.ShouldRefreshTrayConfigs(
            hasCachedConfigs: false,
            lastRefreshUtc: now.AddMinutes(-1),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshTrayConfigs_WithFreshCachedConfigs_ReturnsFalse()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = MainWindowRuntimeLogic.ShouldRefreshTrayConfigs(
            hasCachedConfigs: true,
            lastRefreshUtc: now.AddMinutes(-2),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshTrayConfigs_WithStaleCachedConfigs_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var shouldRefresh = MainWindowRuntimeLogic.ShouldRefreshTrayConfigs(
            hasCachedConfigs: true,
            lastRefreshUtc: now.AddMinutes(-10),
            nowUtc: now,
            refreshInterval: TimeSpan.FromMinutes(5));

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void FormatMonitorOfflineStatus_NoLastUpdate_ReturnsNoDataMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 0, 0);

        var status = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(DateTime.MinValue, now);

        Assert.Equal("Monitor offline — no data received yet", status);
    }

    [Fact]
    public void FormatMonitorOfflineStatus_SecondsElapsed_ReturnsSecondsMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 0, 30);
        var lastUpdate = now.AddSeconds(-12);

        var status = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 12s ago", status);
    }

    [Fact]
    public void FormatMonitorOfflineStatus_MinutesElapsed_ReturnsMinutesMessage()
    {
        var now = new DateTime(2026, 3, 21, 14, 45, 0);
        var lastUpdate = now.AddMinutes(-17);

        var status = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 17m ago", status);
    }

    [Fact]
    public void FormatMonitorOfflineStatus_HoursElapsed_ReturnsHoursMessage()
    {
        var now = new DateTime(2026, 3, 21, 19, 0, 0);
        var lastUpdate = now.AddHours(-3).AddMinutes(-20);

        var status = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(lastUpdate, now);

        Assert.Equal("Monitor offline — last sync 3h ago", status);
    }

    [Fact]
    public void BuildTooltipContent_WithBurstAndWeeklyWindows_IncludesBothLimitAndResetLines()
    {
        var burstReset = new DateTime(2026, 4, 18, 10, 30, 0, DateTimeKind.Utc);
        var weeklyReset = new DateTime(2026, 4, 22, 15, 45, 0, DateTimeKind.Utc);
        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "OpenAI (Codex)",
            IsAvailable = true,
            Description = "Primary plan",
            WindowCards = new List<ProviderUsage>
            {
                new()
                {
                    Name = "5h",
                    WindowKind = WindowKind.Burst,
                    UsedPercent = 40,
                    NextResetTime = burstReset,
                },
                new()
                {
                    Name = "Weekly",
                    WindowKind = WindowKind.Rolling,
                    UsedPercent = 25,
                    NextResetTime = weeklyReset,
                },
            },
        };

        var tooltip = MainWindowRuntimeLogic.BuildTooltipContent(usage, usage.ProviderName!, useRelativeResetTime: false);

        Assert.NotNull(tooltip);
        Assert.Contains("Model provider: codex", tooltip, StringComparison.Ordinal);
        Assert.Contains("5h limit: 60% remaining", tooltip, StringComparison.Ordinal);
        Assert.Contains($"5h resets: {UsageMath.FormatAbsoluteDate(burstReset)}", tooltip, StringComparison.Ordinal);
        Assert.Contains("Weekly limit: 75% remaining", tooltip, StringComparison.Ordinal);
        Assert.Contains($"Weekly resets: {UsageMath.FormatAbsoluteDate(weeklyReset)}", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTooltipContent_WithoutWindowCards_DoesNotIncludeWindowLimitLines()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "minimax-io",
            ProviderName = "MiniMax.io",
            IsAvailable = true,
            Description = "100,000 tokens used / 1,000,000 limit",
            UsedPercent = 10,
        };

        var tooltip = MainWindowRuntimeLogic.BuildTooltipContent(usage, usage.ProviderName!, useRelativeResetTime: false);

        Assert.NotNull(tooltip);
        Assert.Contains("Model provider: minimax-io", tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("resets:", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("limit:", tooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTooltipContent_WithRelativeResetSetting_UsesRelativeResetStrings()
    {
        var burstReset = DateTime.UtcNow.AddDays(2).AddHours(3);
        var weeklyReset = DateTime.UtcNow.AddDays(6).AddHours(4);
        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "OpenAI (Codex)",
            IsAvailable = true,
            WindowCards = new List<ProviderUsage>
            {
                new()
                {
                    Name = "5h",
                    WindowKind = WindowKind.Burst,
                    UsedPercent = 40,
                    NextResetTime = burstReset,
                },
                new()
                {
                    Name = "Weekly",
                    WindowKind = WindowKind.Rolling,
                    UsedPercent = 25,
                    NextResetTime = weeklyReset,
                },
            },
        };

        var tooltip = MainWindowRuntimeLogic.BuildTooltipContent(usage, usage.ProviderName!, useRelativeResetTime: true);

        Assert.NotNull(tooltip);
        Assert.Contains("Model provider: codex", tooltip, StringComparison.Ordinal);
        Assert.Contains($"5h resets: {UsageMath.FormatRelativeTime(burstReset)}", tooltip, StringComparison.Ordinal);
        Assert.Contains($"Weekly resets: {UsageMath.FormatRelativeTime(weeklyReset)}", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveResetWindowLabel_CodingQuotaProviderWithoutExplicitWindow_FallsBackTo5h()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "zai-coding-plan",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            NextResetTime = DateTime.UtcNow.AddHours(2),
        };

        var label = MainWindowRuntimeLogic.ResolveResetWindowLabel(usage);

        Assert.Equal("5h", label);
    }

    [Fact]
    public void ResolveResetWindowLabel_GitHubCopilotMonthlyCard_ReturnsNull()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "github-copilot",
            CardId = "monthly",
            Name = "Monthly Quota",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            NextResetTime = DateTime.UtcNow.AddDays(20),
        };

        var label = MainWindowRuntimeLogic.ResolveResetWindowLabel(usage);

        Assert.Null(label);
    }

    [Fact]
    public void BuildTooltipContent_WithSingleCodingReset_Includes5hResetLine()
    {
        var reset = new DateTime(2026, 4, 18, 10, 30, 0, DateTimeKind.Utc);
        var usage = new ProviderUsage
        {
            ProviderId = "zai-coding-plan",
            ProviderName = "Z.ai Coding Plan",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = "80% remaining",
            NextResetTime = reset,
        };

        var tooltip = MainWindowRuntimeLogic.BuildTooltipContent(usage, usage.ProviderName!, useRelativeResetTime: false);

        Assert.NotNull(tooltip);
        Assert.Contains($"5h resets: {UsageMath.FormatAbsoluteDate(reset)}", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTooltipContent_WithSingleCopilotReset_IncludesGenericResetLine()
    {
        var reset = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var usage = new ProviderUsage
        {
            ProviderId = "github-copilot",
            ProviderName = "GitHub Copilot",
            CardId = "monthly",
            Name = "Monthly Quota",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = "80% remaining",
            NextResetTime = reset,
        };

        var tooltip = MainWindowRuntimeLogic.BuildTooltipContent(usage, usage.ProviderName!, useRelativeResetTime: false);

        Assert.NotNull(tooltip);
        Assert.Contains($"Resets: {UsageMath.FormatAbsoluteDate(reset)}", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTooltipContent_MinimaxCodingPlan_UsesUtcResetText()
    {
        var burstReset = new DateTime(2026, 4, 21, 20, 0, 0, DateTimeKind.Utc);
        var usage = new ProviderUsage
        {
            ProviderId = MinimaxProvider.CodingPlanProviderId,
            ProviderName = "Minimax.io Coding Plan",
            IsAvailable = true,
            WindowCards = new List<ProviderUsage>
            {
                new()
                {
                    ProviderId = MinimaxProvider.CodingPlanProviderId,
                    Name = "5h",
                    WindowKind = WindowKind.Burst,
                    UsedPercent = 10,
                    NextResetTime = burstReset,
                },
            },
        };

        var tooltip = MainWindowRuntimeLogic.BuildTooltipContent(usage, usage.ProviderName!, useRelativeResetTime: false);

        Assert.NotNull(tooltip);
        Assert.Contains("5h resets: Apr 21, 20:00 UTC", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTooltipContent_WithModelName_IncludesModelLine()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "gemini",
            ParentProviderId = "google",
            ProviderName = "Gemini",
            ModelName = "gemini-2.5-pro",
            IsAvailable = true,
        };

        var tooltip = MainWindowRuntimeLogic.BuildTooltipContent(usage, usage.ProviderName!, useRelativeResetTime: false);

        Assert.NotNull(tooltip);
        Assert.Contains("Model provider: google", tooltip, StringComparison.Ordinal);
        Assert.Contains("Model: gemini-2.5-pro", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTooltipContent_UnavailableProvider_DoesNotShowDerivedQuotaFallbacks()
    {
        var reset = new DateTime(2026, 4, 21, 20, 0, 0, DateTimeKind.Utc);
        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "OpenAI (Codex)",
            IsAvailable = false,
            State = ProviderUsageState.Unavailable,
            Description = "Temporarily paused after repeated failures",
            UsedPercent = 0,
            PeriodDuration = TimeSpan.FromDays(7),
            NextResetTime = reset,
            WindowCards = new List<ProviderUsage>
            {
                new()
                {
                    ProviderId = "codex",
                    Name = "5h",
                    WindowKind = WindowKind.Burst,
                    UsedPercent = 0,
                    NextResetTime = reset,
                },
            },
        };

        var tooltip = MainWindowRuntimeLogic.BuildTooltipContent(usage, usage.ProviderName!, useRelativeResetTime: false);

        Assert.NotNull(tooltip);
        Assert.Contains("Status: Inactive", tooltip, StringComparison.Ordinal);
        Assert.Contains("Temporarily paused after repeated failures", tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("limit:", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resets:", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Daily budget:", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Expected by now:", tooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveCardResetDisplay_DualBarsVisible_UsesBurstReset()
    {
        var burstReset = new DateTime(2026, 4, 21, 20, 0, 0, DateTimeKind.Utc);
        var weeklyReset = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc);
        var usage = new ProviderUsage
        {
            ProviderId = MinimaxProvider.CodingPlanProviderId,
            ProviderName = "Minimax.io Coding Plan",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsedPercent = 20,
            WindowCards = new List<ProviderUsage>
            {
                new()
                {
                    ProviderId = MinimaxProvider.CodingPlanProviderId,
                    Name = "5h",
                    WindowKind = WindowKind.Burst,
                    UsedPercent = 25,
                    NextResetTime = burstReset,
                },
                new()
                {
                    ProviderId = MinimaxProvider.CodingPlanProviderId,
                    Name = "Weekly",
                    WindowKind = WindowKind.Rolling,
                    UsedPercent = 30,
                    NextResetTime = weeklyReset,
                },
            },
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true, enablePaceAdjustment: true);

        var (resetTime, resetLabel) = MainWindowRuntimeLogic.ResolveCardResetDisplay(
            usage,
            presentation,
            showDualQuotaBars: true,
            dualQuotaSingleBarMode: DualQuotaSingleBarMode.Rolling);

        Assert.Equal(burstReset, resetTime);
        Assert.Equal("5h", resetLabel);
    }

    [Fact]
    public void ResolveCardResetDisplay_DualBarsCollapsed_RespectsSelectedWindow()
    {
        var burstReset = new DateTime(2026, 4, 21, 20, 0, 0, DateTimeKind.Utc);
        var weeklyReset = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc);
        var usage = new ProviderUsage
        {
            ProviderId = MinimaxProvider.CodingPlanProviderId,
            ProviderName = "Minimax.io Coding Plan",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsedPercent = 20,
            WindowCards = new List<ProviderUsage>
            {
                new()
                {
                    ProviderId = MinimaxProvider.CodingPlanProviderId,
                    Name = "5h",
                    WindowKind = WindowKind.Burst,
                    UsedPercent = 25,
                    NextResetTime = burstReset,
                },
                new()
                {
                    ProviderId = MinimaxProvider.CodingPlanProviderId,
                    Name = "Weekly",
                    WindowKind = WindowKind.Rolling,
                    UsedPercent = 30,
                    NextResetTime = weeklyReset,
                },
            },
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true, enablePaceAdjustment: true);

        var (resetTime, resetLabel) = MainWindowRuntimeLogic.ResolveCardResetDisplay(
            usage,
            presentation,
            showDualQuotaBars: false,
            dualQuotaSingleBarMode: DualQuotaSingleBarMode.Rolling);

        Assert.Equal(weeklyReset, resetTime);
        Assert.Equal("Weekly", resetLabel);
    }

    [Fact]
    public void Create_MinimaxCodingPlanDualBars_ComputesPaceBadges()
    {
        var now = DateTime.UtcNow;
        var usage = new ProviderUsage
        {
            ProviderId = MinimaxProvider.CodingPlanProviderId,
            ProviderName = "Minimax.io Coding Plan",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsedPercent = 20,
            WindowCards = new List<ProviderUsage>
            {
                new()
                {
                    ProviderId = MinimaxProvider.CodingPlanProviderId,
                    Name = "5h",
                    WindowKind = WindowKind.Burst,
                    UsedPercent = 20,
                    NextResetTime = now.AddHours(2),
                },
                new()
                {
                    ProviderId = MinimaxProvider.CodingPlanProviderId,
                    Name = "Weekly",
                    WindowKind = WindowKind.Rolling,
                    UsedPercent = 35,
                    NextResetTime = now.AddDays(4),
                },
            },
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true, enablePaceAdjustment: true);

        Assert.NotNull(presentation.DualBar);
        Assert.True(presentation.DualBar!.Primary.PaceColor.IsPaceAdjusted);
        Assert.True(presentation.DualBar.Secondary.PaceColor.IsPaceAdjusted);
        Assert.False(string.IsNullOrWhiteSpace(presentation.DualBar.Primary.PaceColor.BadgeText));
        Assert.False(string.IsNullOrWhiteSpace(presentation.DualBar.Secondary.PaceColor.BadgeText));
    }
}
