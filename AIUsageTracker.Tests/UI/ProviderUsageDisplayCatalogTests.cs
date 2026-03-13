// <copyright file="ProviderUsageDisplayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.DisplayableUsages.Count);
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "antigravity", StringComparison.Ordinal));
        Assert.False(preparation.HasAggregateParent);
    }

    [Fact]
    public void PrepareForMainWindow_HidesLegacyOpenAiProvider()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "openai", IsAvailable = true },
            new() { ProviderId = "codex", IsAvailable = true },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("codex", preparation.DisplayableUsages[0].ProviderId);
    }

    [Fact]
    public void PrepareForMainWindow_KeepsAntigravityChildren_WhenParentExists()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "antigravity", IsAvailable = true },
            new() { ProviderId = "antigravity.gemini-pro", IsAvailable = true },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.DisplayableUsages.Count);
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "antigravity", StringComparison.Ordinal));
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "antigravity.gemini-pro", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_KeptsCodexChildren_WhenParentExists()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "codex.spark", IsAvailable = true },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.DisplayableUsages.Count);
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_HidesUnknownProviders_ButKeepsKnownProviders()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "legacy-unknown-provider", IsAvailable = true },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("codex", preparation.DisplayableUsages[0].ProviderId);
    }

    [Fact]
    public void CreateAggregateDetailUsages_ReturnsEmpty_WhenProviderDoesNotUseSyntheticChildren()
    {
        var detail = new ProviderUsageDetail
        {
            Name = "Gemini 3 Flash",
        };
        detail.SetPercentageValue(100, PercentageValueSemantic.Remaining);

        var parent = new ProviderUsage
        {
            ProviderId = "antigravity",
            IsAvailable = true,
            Details = new List<ProviderUsageDetail> { detail },
        };

        Assert.Empty(ProviderUsageDisplayCatalog.CreateAggregateDetailUsages(parent));
    }

    [Fact]
    public void PrepareForMainWindow_UsesProviderMetadata_ForCodexFamilyBehavior()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "codex.spark", IsAvailable = true },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.DisplayableUsages.Count);
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_DoesNotExpandGeminiDetailsInUiLayer()
    {
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "gemini-cli",
                ProviderName = "Gemini CLI",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Details = new List<ProviderUsageDetail>
                {
                    new()
                    {
                        Name = "Requests / Minute",
                        Used = "67.9%",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Primary,
                        NextResetTime = new DateTime(2026, 3, 12, 14, 38, 28),
                    },
                    new()
                    {
                        Name = "Requests / Day",
                        Used = "97.5%",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Secondary,
                        NextResetTime = new DateTime(2026, 3, 12, 14, 35, 2),
                    },
                    new()
                    {
                        Name = "Requests / Hour",
                        Used = "88.0%",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Spark,
                        NextResetTime = new DateTime(2026, 3, 12, 15, 10, 0),
                    },
                },
            },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        var displayable = Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("gemini-cli", displayable.ProviderId);
        Assert.Equal("Gemini CLI", displayable.ProviderName);
    }

    [Fact]
    public void PrepareForMainWindow_KeepsExplicitGeminiChildBarsOnTopLevel()
    {
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "gemini-cli",
                ProviderName = "Gemini CLI",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Details = new List<ProviderUsageDetail>
                {
                    new()
                    {
                        Name = "Requests / Minute",
                        Used = "67.9%",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Primary,
                    },
                    new()
                    {
                        Name = "Requests / Hour",
                        Used = "88.0%",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Secondary,
                    },
                    new()
                    {
                        Name = "Gemini 3.1 Pro Preview",
                        ModelName = "gemini-3.1-pro-preview",
                        Used = "0.0%",
                        DetailType = ProviderUsageDetailType.Model,
                        QuotaBucketKind = WindowKind.None,
                    },
                },
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

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Equal(3, preparation.DisplayableUsages.Count);
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "gemini-cli.minute", StringComparison.Ordinal));
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "gemini-cli.hourly", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_PrefersGeminiUsageWithDetails_WhenDuplicateProviderEntriesExist()
    {
        var stale = new ProviderUsage
        {
            ProviderId = "gemini-cli",
            ProviderName = "Gemini CLI",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            FetchedAt = new DateTime(2026, 3, 12, 10, 0, 0),
            Details = null,
        };

        var fresh = new ProviderUsage
        {
            ProviderId = "gemini-cli",
            ProviderName = "Gemini CLI",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            FetchedAt = new DateTime(2026, 3, 12, 10, 5, 0),
            Details = new List<ProviderUsageDetail>
            {
                new()
                {
                    Name = "Requests / Minute",
                    Used = "67.9%",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Primary,
                },
                new()
                {
                    Name = "Requests / Hour",
                    Used = "88.0%",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Secondary,
                },
                new()
                {
                    Name = "Requests / Day",
                    Used = "97.5%",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Spark,
                },
            },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(new[] { stale, fresh });

        var gemini = Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("gemini-cli", gemini.ProviderId);
        Assert.NotNull(gemini.Details);
        Assert.Equal(3, gemini.Details!.Count);
    }
}
