// <copyright file="GroupedUsageProjectionServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Tests.Services;

public sealed class GroupedUsageProjectionServiceTests
{
    [Fact]
    public void Build_AntigravityWithFlatCardUsage_ProjectsCardIdAsModel()
    {
        // Antigravity emits flat cards with CardId set; BuildModelsFromFlatCards picks them up.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "antigravity",
                CardId = "gemini-3-flash",
                Name = "Gemini 3 Flash",
                ProviderName = "Gemini 3 Flash",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 0,
                RequestsUsed = 0,
                RequestsAvailable = 135,
                DisplayAsFraction = true,
                Description = "100% Remaining",
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("Google Antigravity", provider.ProviderName);
        var model = Assert.Single(provider.Models);
        Assert.Equal("gemini-3-flash", model.ModelId);
        Assert.Equal("Gemini 3 Flash", model.ModelName);
        Assert.Equal(100, model.RemainingPercentage);
        Assert.Equal(0, model.UsedPercentage);
        Assert.Equal("100% Remaining", model.Description);
    }

    [Fact]
    public void Build_WhenMostRecentEntryIsError_PrimaryUsageReflectsErrorState()
    {
        // Older successful entry + newer error entry — primary must be the newest so the
        // error reason is surfaced rather than showing stale successful data.
        var old = DateTime.UtcNow.AddHours(-19);
        var now = DateTime.UtcNow;

        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                IsAvailable = true,
                UsedPercent = 42,
                Description = "58% remaining",
                FetchedAt = old,
            },
            new ProviderUsage
            {
                ProviderId = "codex",
                IsAvailable = false,
                UsedPercent = 0,
                Description = "HTTP 401: Unauthorized",
                FetchedAt = now,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        // Description must come from the most recent entry, not the stale successful one.
        Assert.Equal("HTTP 401: Unauthorized", provider.Description);
    }

    [Fact]
    public void Build_KimiWithWindowKindCards_ProjectsAsProviderDetailsNotModels()
    {
        // Kimi emits flat cards with CardId + WindowKind (Rolling/Burst).
        // Because WindowKind != None, they must NOT be projected as Models (which would
        // produce separate flat cards). They must flow through as ProviderDetails so the
        // UI renders them as dual quota bars on a single parent card.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "kimi-for-coding",
                CardId = "weekly",
                Name = "Weekly Limit",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 25,
                RequestsUsed = 25,
                RequestsAvailable = 100,
                NextResetTime = DateTime.UtcNow.AddDays(5),
                PeriodDuration = TimeSpan.FromDays(7),
            },
            new ProviderUsage
            {
                ProviderId = "kimi-for-coding",
                CardId = "5-hour-limit",
                Name = "5 Hour Limit",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 0,
                RequestsUsed = 0,
                RequestsAvailable = 50,
                NextResetTime = DateTime.UtcNow.AddHours(3),
                PeriodDuration = TimeSpan.FromHours(5),
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Empty(provider.Models); // not flat model cards
        Assert.Equal(2, provider.ProviderDetails.Count); // both appear as quota-window details
        Assert.Contains(provider.ProviderDetails, d => d.WindowKind == WindowKind.Rolling);
        Assert.Contains(provider.ProviderDetails, d => d.WindowKind == WindowKind.Burst);
    }

    [Fact]
    public void Build_ClaudeCodeCards_ProjectsAllAsModels_WhenAllWindowKindNone()
    {
        // Claude Code cards all have WindowKind.None — each gets its own flat card in the UI.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "claude-code",
                CardId = "current-session",
                Name = "Current Session",
                WindowKind = WindowKind.None,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Usage,
                UsedPercent = 14,
            },
            new ProviderUsage
            {
                ProviderId = "claude-code",
                CardId = "sonnet",
                Name = "Sonnet",
                WindowKind = WindowKind.None,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Usage,
                UsedPercent = 73,
            },
            new ProviderUsage
            {
                ProviderId = "claude-code",
                CardId = "all-models",
                Name = "All Models",
                WindowKind = WindowKind.None,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Usage,
                UsedPercent = 73,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal(3, provider.Models.Count); // all three become flat model cards
        Assert.Contains(provider.Models, m => m.ModelId == "current-session");
        Assert.Contains(provider.Models, m => m.ModelId == "sonnet");
        Assert.Contains(provider.Models, m => m.ModelId == "all-models");
    }

    [Fact]
    public void Build_CodexAndSpark_ProjectAsOneGroupWithFourFlatCards()
    {
        // codex.spark is a child of codex (FlatWindowCards family mode).
        // All 4 quota windows (burst, weekly, spark.burst, spark.weekly) are
        // projected as flat model cards within the single "codex" group.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                CardId = "burst",
                Name = "5-hour quota",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 40,
                PeriodDuration = TimeSpan.FromHours(5),
            },
            new ProviderUsage
            {
                ProviderId = "codex",
                CardId = "weekly",
                Name = "Weekly quota",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 72,
                PeriodDuration = TimeSpan.FromDays(7),
            },
            new ProviderUsage
            {
                ProviderId = "codex.spark",
                CardId = "spark.burst",
                Name = "Spark 5-hour quota",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 12,
                PeriodDuration = TimeSpan.FromHours(5),
            },
            new ProviderUsage
            {
                ProviderId = "codex.spark",
                CardId = "spark.weekly",
                Name = "Spark weekly quota",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 8,
                PeriodDuration = TimeSpan.FromDays(7),
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        // One "codex" group containing all 4 flat window cards.
        var codex = Assert.Single(snapshot.Providers);
        Assert.Equal("codex", codex.ProviderId);
        Assert.Equal(4, codex.Models.Count);
        Assert.Contains(codex.Models, m => m.ModelId == "burst");
        Assert.Contains(codex.Models, m => m.ModelId == "weekly");
        Assert.Contains(codex.Models, m => m.ModelId == "spark.burst");
        Assert.Contains(codex.Models, m => m.ModelId == "spark.weekly");
    }
}
