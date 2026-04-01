// <copyright file="GroupedUsageProjectionServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor.Tests;

public sealed class GroupedUsageProjectionServiceTests
{
    [Fact]
    public void Build_ProjectsGeminiModelCards_AsFlatCardModels()
    {
        // Gemini now emits one flat card per model (no parent card, no Details).
        // Each flat card has ModelName set and CardId = "model-<modelId>".
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "gemini-cli",
                ProviderName = "Gemini CLI",
                IsAvailable = true,
                IsQuotaBased = true,
                CardId = "model-gemini-2.5-flash-lite",
                GroupId = "gemini-cli",
                Name = "Gemini 2.5 Flash Lite",
                ModelName = "gemini-2.5-flash-lite",
                UsedPercent = 3.3,
                RequestsUsed = 3.3,
                RequestsAvailable = 100,
                FetchedAt = DateTime.UtcNow,
            },
            new ProviderUsage
            {
                ProviderId = "gemini-cli",
                ProviderName = "Gemini CLI",
                IsAvailable = true,
                IsQuotaBased = true,
                CardId = "model-gemini-3-flash-preview",
                GroupId = "gemini-cli",
                Name = "Gemini 3 Flash Preview",
                ModelName = "gemini-3-flash-preview",
                UsedPercent = 40,
                RequestsUsed = 40,
                RequestsAvailable = 100,
                FetchedAt = DateTime.UtcNow,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("gemini-cli", provider.ProviderId);
        Assert.Equal(2, provider.Models.Count);
        Assert.Contains(provider.Models, m => string.Equals(m.ModelId, "model-gemini-2.5-flash-lite", StringComparison.Ordinal));
        Assert.Contains(provider.Models, m => string.Equals(m.ModelId, "model-gemini-3-flash-preview", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_DoesNotUseDerivedRowsAsModelFallback_WhenModelDetailsAreMissing()
    {
        // Codex (UseChildProviderRowsForGroupedModels = false) with no CardIds:
        // neither BuildModelsFromFlatCards nor BuildModelsFromExplicitChildRows fires.
        // Models list is empty.
        var now = DateTime.UtcNow;
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                ProviderName = "OpenAI (Codex)",
                IsAvailable = true,
                IsQuotaBased = true,
                RequestsUsed = 50,
                RequestsAvailable = 100,
                UsedPercent = 50,
                FetchedAt = now,
            },
            new ProviderUsage
            {
                ProviderId = "codex.spark",
                ProviderName = "OpenAI (GPT-5.3-Codex-Spark)",
                IsAvailable = true,
                IsQuotaBased = true,
                RequestsUsed = 10,
                RequestsAvailable = 100,
                UsedPercent = 10,
                FetchedAt = now,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("codex", provider.ProviderId);
        Assert.Equal(0, provider.Models.Count);
        Assert.Empty(provider.Models);
    }

    [Fact]
    public void Build_KeepsProviderWithEmptyModelArray_WhenNoModelDataExists()
    {
        // A usage with no CardId and no window cards → empty models, included in snapshot.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "github-copilot",
                ProviderName = "GitHub Copilot",
                IsAvailable = false,
                IsQuotaBased = true,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsedPercent = 0,
                Description = "Not authenticated",
                FetchedAt = DateTime.UtcNow,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("github-copilot", provider.ProviderId);
        Assert.Empty(provider.Models);
        Assert.Equal(0, provider.Models.Count);
    }

    [Fact]
    public void Build_KimiUsage_PopulatesProviderDetails_FromWindowFlatCards()
    {
        // Kimi now emits flat cards with WindowKind set.
        // The projection collects cards with WindowKind != None as ProviderDetails.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "kimi-for-coding",
                CardId = "weekly",
                GroupId = "kimi-for-coding",
                Name = "Weekly Limit",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 25,
                RequestsUsed = 25,
                RequestsAvailable = 100,
                FetchedAt = DateTime.UtcNow,
            },
            new ProviderUsage
            {
                ProviderId = "kimi-for-coding",
                CardId = "5h-limit",
                GroupId = "kimi-for-coding",
                Name = "5h Limit",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 0,
                RequestsUsed = 0,
                RequestsAvailable = 100,
                FetchedAt = DateTime.UtcNow,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal(2, provider.ProviderDetails.Count);
        Assert.Contains(provider.ProviderDetails, d => d.WindowKind == WindowKind.Rolling && string.Equals(d.Name, "Weekly Limit", StringComparison.Ordinal));
        Assert.Contains(provider.ProviderDetails, d => d.WindowKind == WindowKind.Burst && string.Equals(d.Name, "5h Limit", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_CodexAndSpark_ProjectAsOneGroupWithFourFlatCards()
    {
        // codex.spark is a child of codex (FlatWindowCards family mode).
        // All 4 quota windows are projected as flat model cards within the single "codex" group.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                CardId = "burst",
                GroupId = "codex",
                Name = "5-hour quota",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 0,
                FetchedAt = DateTime.UtcNow,
            },
            new ProviderUsage
            {
                ProviderId = "codex",
                CardId = "weekly",
                GroupId = "codex",
                Name = "Weekly quota",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 98,
                FetchedAt = DateTime.UtcNow,
            },
            new ProviderUsage
            {
                ProviderId = "codex.spark",
                CardId = "spark.burst",
                GroupId = "codex",
                Name = "Spark 5-hour quota",
                WindowKind = WindowKind.Burst,
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 19,
                FetchedAt = DateTime.UtcNow,
            },
            new ProviderUsage
            {
                ProviderId = "codex.spark",
                CardId = "spark.weekly",
                GroupId = "codex",
                Name = "Spark weekly quota",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 5,
                FetchedAt = DateTime.UtcNow,
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

    [Fact]
    public void Build_DeepSeekCurrencyCards_AreNotProjectedAsModels()
    {
        // DeepSeek emits flat currency/balance cards (IsCurrencyUsage = true, CardId = "balance-usd" etc.).
        // These must NOT be projected as model rows — they are balance display cards, not quota windows.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "deepseek",
                CardId = "balance-usd",
                GroupId = "deepseek",
                Name = "Balance (USD)",
                IsCurrencyUsage = true,
                IsQuotaBased = false,
                IsAvailable = true,
                UsedPercent = 0,
                Description = "$12.34 (10.00 topped-up + 2.34 granted)",
                FetchedAt = DateTime.UtcNow,
            },
            new ProviderUsage
            {
                ProviderId = "deepseek",
                CardId = "balance-cny",
                GroupId = "deepseek",
                Name = "Balance (CNY)",
                IsCurrencyUsage = true,
                IsQuotaBased = false,
                IsAvailable = true,
                UsedPercent = 0,
                Description = "¥88.00 (80.00 topped-up + 8.00 granted)",
                FetchedAt = DateTime.UtcNow,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("deepseek", provider.ProviderId);
        Assert.Empty(provider.Models);
    }

    [Fact]
    public void Build_KimiUsage_FiltersToWindowCards_InProviderDetails()
    {
        // Cards with WindowKind.None (e.g., credit-type cards, labels) are excluded from ProviderDetails.
        // Only WindowKind != None passes through.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "kimi-for-coding",
                CardId = "credits",
                GroupId = "kimi-for-coding",
                Name = "Credits",
                WindowKind = WindowKind.None, // not a quota window
                IsAvailable = true,
                IsQuotaBased = false,
                UsedPercent = 10,
                FetchedAt = DateTime.UtcNow,
            },
            new ProviderUsage
            {
                ProviderId = "kimi-for-coding",
                CardId = "weekly",
                GroupId = "kimi-for-coding",
                Name = "Weekly Limit",
                WindowKind = WindowKind.Rolling,
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 10,
                FetchedAt = DateTime.UtcNow,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Single(provider.ProviderDetails); // only WindowKind.Rolling passes; WindowKind.None is excluded
    }
}
