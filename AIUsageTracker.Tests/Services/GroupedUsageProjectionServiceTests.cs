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
    public void Build_KimiWithWindowKindCards_ProjectsNoModels()
    {
        // Kimi emits WindowKind cards only. In the strict models-only grouped contract,
        // WindowKind != None rows are not projected as grouped models.
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
        Assert.Empty(provider.Models);
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
        Assert.Contains(provider.Models, m => string.Equals(m.ModelId, "current-session", StringComparison.Ordinal));
        Assert.Contains(provider.Models, m => string.Equals(m.ModelId, "sonnet", StringComparison.Ordinal));
        Assert.Contains(provider.Models, m => string.Equals(m.ModelId, "all-models", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_CodexAndSpark_WindowKindRows_ProjectAsTwoSeparateGroupsWithoutModels()
    {
        // codex and codex.spark are standalone owner providers (FamilyMode = Standalone).
        // Each emits a Burst + Rolling pair, resulting in two separate groups with no grouped models.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                CardId = "burst",
                GroupId = "codex",
                Name = "5h",
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
                GroupId = "codex",
                Name = "Weekly",
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
                GroupId = "codex.spark",
                Name = "5h",
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
                GroupId = "codex.spark",
                Name = "Weekly",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 8,
                PeriodDuration = TimeSpan.FromDays(7),
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        // Two separate groups — each with no grouped models.
        Assert.Equal(2, snapshot.Providers.Count);

        var codex = Assert.Single(snapshot.Providers, p => string.Equals(p.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Empty(codex.Models);

        var spark = Assert.Single(snapshot.Providers, p => string.Equals(p.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.Empty(spark.Models);
    }

    [Fact]
    public void Build_MinimaxIoAndCodingPlan_ProjectAsSeparateProviders()
    {
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "minimax-io",
                ProviderName = "MiniMax.io",
                IsAvailable = true,
                UsedPercent = 25,
                RequestsUsed = 250,
                RequestsAvailable = 1000,
            },
            new ProviderUsage
            {
                ProviderId = "minimax-coding-plan",
                ProviderName = "Minimax.io Coding Plan",
                CardId = "burst",
                Name = "5h",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 40,
                RequestsUsed = 40,
                RequestsAvailable = 100,
                PeriodDuration = TimeSpan.FromHours(5),
            },
            new ProviderUsage
            {
                ProviderId = "minimax-coding-plan",
                ProviderName = "Minimax.io Coding Plan",
                CardId = "weekly",
                Name = "Weekly",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 30,
                RequestsUsed = 150,
                RequestsAvailable = 500,
                PeriodDuration = TimeSpan.FromDays(7),
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var minimaxIo = Assert.Single(snapshot.Providers, p => string.Equals(p.ProviderId, "minimax-io", StringComparison.Ordinal));
        var minimaxCoding = Assert.Single(snapshot.Providers, p => string.Equals(p.ProviderId, "minimax-coding-plan", StringComparison.Ordinal));

        Assert.Equal("MiniMax.io", minimaxIo.ProviderName);
        Assert.Equal("Minimax.io Coding Plan", minimaxCoding.ProviderName);
        Assert.Empty(minimaxIo.Models);
        Assert.Empty(minimaxCoding.Models);
    }
}
