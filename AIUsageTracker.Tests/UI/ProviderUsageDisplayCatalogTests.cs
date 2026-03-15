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
    public void ExpandSyntheticAggregateChildren_YieldsParentAsIs_WhenProviderDoesNotUseSyntheticChildren()
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

        var result = ProviderUsageDisplayCatalog.ExpandSyntheticAggregateChildren(
            new[] { parent },
            Array.Empty<string>()).ToList();

        Assert.Single(result);
        Assert.Equal("antigravity", result[0].ProviderId);
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
                        QuotaBucketKind = WindowKind.Burst,
                        NextResetTime = new DateTime(2026, 3, 12, 14, 38, 28),
                    },
                    new()
                    {
                        Name = "Requests / Day",
                        Used = "97.5%",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Rolling,
                        NextResetTime = new DateTime(2026, 3, 12, 14, 35, 2),
                    },
                    new()
                    {
                        Name = "Requests / Hour",
                        Used = "88.0%",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.ModelSpecific,
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
                        QuotaBucketKind = WindowKind.Burst,
                    },
                    new()
                    {
                        Name = "Requests / Hour",
                        Used = "88.0%",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Rolling,
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

    // ── Synthetic aggregate children (claude-code) ─────────────────────────────
    // Guards that each synthetic child card carries the correct per-window
    // NextResetTime from its originating detail row.

    [Fact]
    public void ExpandSyntheticAggregateChildren_SetsPerWindowResetTime_OnEachChildCard()
    {
        var burstReset = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var weeklyReset = new DateTime(2026, 4, 7, 0, 0, 0, DateTimeKind.Utc);

        var burstDetail = new ProviderUsageDetail
        {
            Name = "Current Session",
            DetailType = ProviderUsageDetailType.Model,
            QuotaBucketKind = WindowKind.Burst,
            NextResetTime = burstReset,
        };
        burstDetail.SetPercentageValue(35.0, PercentageValueSemantic.Used);

        var rollingDetail = new ProviderUsageDetail
        {
            Name = "All Models",
            DetailType = ProviderUsageDetailType.Model,
            QuotaBucketKind = WindowKind.Rolling,
            NextResetTime = weeklyReset,
        };
        rollingDetail.SetPercentageValue(42.0, PercentageValueSemantic.Used);

        var parent = new ProviderUsage
        {
            ProviderId = "claude-code",
            IsAvailable = true,
            IsQuotaBased = true,
            Details = new List<ProviderUsageDetail> { burstDetail, rollingDetail },
        };

        var children = ProviderUsageDisplayCatalog.ExpandSyntheticAggregateChildren(
            new[] { parent },
            Array.Empty<string>()).ToList();

        Assert.Equal(2, children.Count);

        var currentSession = children.Single(c =>
            string.Equals(c.ProviderId, "claude-code.current-session", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(burstReset, currentSession.NextResetTime);

        var allModels = children.Single(c =>
            string.Equals(c.ProviderId, "claude-code.all-models", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(weeklyReset, allModels.NextResetTime);
    }

    [Fact]
    public void ExpandSyntheticAggregateChildren_CreatesCorrectProviderIds_ForAllFourClaudeCodeWindows()
    {
        var weeklyReset = new DateTime(2026, 4, 7, 0, 0, 0, DateTimeKind.Utc);
        var burstReset = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);

        ProviderUsageDetail MakeDetail(string name, WindowKind kind, DateTime resetTime)
        {
            var d = new ProviderUsageDetail
            {
                Name = name,
                DetailType = ProviderUsageDetailType.Model,
                QuotaBucketKind = kind,
                NextResetTime = resetTime,
            };
            d.SetPercentageValue(25.0, PercentageValueSemantic.Used);
            return d;
        }

        var parent = new ProviderUsage
        {
            ProviderId = "claude-code",
            IsAvailable = true,
            IsQuotaBased = true,
            Details = new List<ProviderUsageDetail>
            {
                MakeDetail("Current Session", WindowKind.Burst, burstReset),
                MakeDetail("Sonnet", WindowKind.ModelSpecific, weeklyReset),
                MakeDetail("Opus", WindowKind.ModelSpecific, weeklyReset),
                MakeDetail("All Models", WindowKind.Rolling, weeklyReset),
            },
        };

        var children = ProviderUsageDisplayCatalog.ExpandSyntheticAggregateChildren(
            new[] { parent },
            Array.Empty<string>()).ToList();

        Assert.Equal(4, children.Count);
        Assert.Contains(children, c => string.Equals(c.ProviderId, "claude-code.current-session", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(children, c => string.Equals(c.ProviderId, "claude-code.sonnet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(children, c => string.Equals(c.ProviderId, "claude-code.opus", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(children, c => string.Equals(c.ProviderId, "claude-code.all-models", StringComparison.OrdinalIgnoreCase));

        // Each 7-day child must carry the weekly reset time, not the burst reset
        foreach (var child in children.Where(c => !c.ProviderId!.EndsWith("current-session", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Equal(weeklyReset, child.NextResetTime);
        }
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
                    QuotaBucketKind = WindowKind.Burst,
                },
                new()
                {
                    Name = "Requests / Hour",
                    Used = "88.0%",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Rolling,
                },
                new()
                {
                    Name = "Requests / Day",
                    Used = "97.5%",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.ModelSpecific,
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
