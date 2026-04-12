// <copyright file="ProviderUsageDisplayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

#pragma warning disable CS0618 // Used/UsedPercent: legacy fields set in test fixtures

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderUsageDisplayCatalogTests
{
    [Fact]
    public void PrepareForMainWindow_KeepsUnavailableParentsAndDeduplicatesProviders()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "codex", IsAvailable = false },
            new() { ProviderId = "antigravity", IsAvailable = false },
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.Count);
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "antigravity", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_HidesLegacyOpenAiProvider()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "openai", IsAvailable = true },
            new() { ProviderId = "codex", IsAvailable = true },
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Single(preparation);
        Assert.Equal("codex", preparation[0].ProviderId);
    }

    [Fact]
    public void PrepareForMainWindow_KeepsAntigravityChildren_WhenParentExists()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "antigravity", IsAvailable = true },
            new() { ProviderId = "antigravity.gemini-pro", IsAvailable = true },
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.Count);
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "antigravity", StringComparison.Ordinal));
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "antigravity.gemini-pro", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_KeptsCodexChildren_WhenParentExists()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "codex.spark", IsAvailable = true },
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.Count);
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_HidesUnknownProviders_ButKeepsKnownProviders()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "legacy-unknown-provider", IsAvailable = true },
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Single(preparation);
        Assert.Equal("codex", preparation[0].ProviderId);
    }

    [Fact]
    public void PrepareForMainWindow_UsesProviderMetadata_ForCodexFamilyBehavior()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "codex.spark", IsAvailable = true },
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.Count);
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_DoesNotExpandGeminiDetailsInUiLayer()
    {
        // Gemini CLI has window cards but PrepareForMainWindow does not expand them —
        // child rows come from flat ProviderUsage cards in the snapshot, not from window cards.
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "gemini-cli",
                ProviderName = "Gemini CLI",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                WindowCards = new[]
                {
                    new ProviderUsage { ProviderId = "gemini-cli", Name = "Requests / Minute", WindowKind = WindowKind.Burst,   UsedPercent = 67.9 },
                    new ProviderUsage { ProviderId = "gemini-cli", Name = "Requests / Day",    WindowKind = WindowKind.Rolling, UsedPercent = 97.5 },
                },
            },
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        var displayable = Assert.Single(preparation);
        Assert.Equal("gemini-cli", displayable.ProviderId);
        Assert.Equal("Gemini CLI", displayable.ProviderName);
    }

    [Fact]
    public void PrepareForMainWindow_KeepsExplicitGeminiChildBarsOnTopLevel()
    {
        // Explicit flat child cards (gemini-cli.minute, gemini-cli.hourly) are kept as top-level items.
        // PrepareForMainWindow does not suppress them — they are independent flat cards in the snapshot.
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "gemini-cli",
                ProviderName = "Gemini CLI",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
            },
            new()
            {
                ProviderId = "gemini-cli.minute",
                ProviderName = "Gemini CLI (Minute)",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
            },
            new()
            {
                ProviderId = "gemini-cli.hourly",
                ProviderName = "Gemini CLI (Hourly)",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
            },
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Equal(3, preparation.Count);
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "gemini-cli.minute", StringComparison.Ordinal));
        Assert.Contains(preparation, usage => string.Equals(usage.ProviderId, "gemini-cli.hourly", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_PrefersNewerEntry_WhenDuplicateProviderEntriesExist()
    {
        // When two entries share the same ProviderId, the one with a later FetchedAt
        // and a NextResetTime (higher selection score) is preferred.
        var older = new ProviderUsage
        {
            ProviderId = "gemini-cli",
            ProviderName = "Gemini CLI",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            FetchedAt = new DateTime(2026, 3, 12, 10, 0, 0),
            NextResetTime = null,
        };

        var newer = new ProviderUsage
        {
            ProviderId = "gemini-cli",
            ProviderName = "Gemini CLI",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            FetchedAt = new DateTime(2026, 3, 12, 10, 5, 0),
            NextResetTime = new DateTime(2026, 3, 13, 0, 0, 0),
        };

        var preparation = MainWindowRuntimeLogic.PrepareForMainWindow(new[] { older, newer });

        var gemini = Assert.Single(preparation);
        Assert.Equal("gemini-cli", gemini.ProviderId);
        // The preferred entry has NextResetTime set
        Assert.NotNull(gemini.NextResetTime);
    }

}


