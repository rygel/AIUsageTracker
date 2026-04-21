// <copyright file="ProviderCardPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderCardPresentationCatalogTests
{
    [Fact]
    public void Create_ReturnsMissingStatus_ForMissingKeyDescription()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            Description = "API key not found",
            IsAvailable = false,
            State = ProviderUsageState.Missing,
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.True(presentation.IsMissing);
        Assert.Contains("not found", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProviderCardStatusTone.Missing, presentation.StatusTone);
        Assert.False(presentation.ShouldHaveProgress);
    }

    [Fact]
    public void Create_RendersAntigravityAsNormalQuotaProvider_WhenDescriptionMissing()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "antigravity",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 0, // 0% used → 100% remaining
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal("100% remaining", presentation.StatusText);
        Assert.True(presentation.ShouldHaveProgress);
    }

    [Fact]
    public void Create_ShowsProgress_ForClaudeCodeCard()
    {
        // claude-code is a standalone quota-based provider — it must show a progress bar.
        var usage = new ProviderUsage
        {
            ProviderId = "claude-code",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 35, // 35% used → 65% remaining
            Description = "65% Remaining",
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.True(presentation.ShouldHaveProgress);
        Assert.Equal(35, presentation.UsedPercent);
        Assert.Equal(65, presentation.RemainingPercent);
    }

    [Fact]
    public void Create_FormatsQuotaFractionStatus_WhenDisplayAsFraction()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "synthetic",
            IsAvailable = true,
            IsQuotaBased = true,
            DisplayAsFraction = true,
            RequestsUsed = 40,
            RequestsAvailable = 100,
            UsedPercent = 40,
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal("60 / 100 remaining", presentation.StatusText);
        Assert.True(presentation.ShouldHaveProgress);
        Assert.Equal(40, presentation.UsedPercent);
        Assert.Equal(60, presentation.RemainingPercent);
    }

    [Fact]
    public void Create_KeepsDescription_ForStatusUsage()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "mistral",
            Description = "Connected",
            IsAvailable = true,
            IsQuotaBased = true,
            IsStatusOnly = true,
            UsedPercent = 70,
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        Assert.Equal("Connected", presentation.StatusText);
        Assert.False(presentation.ShouldHaveProgress);
    }

    [Fact]
    public void Create_FormatsUsagePlanPercentStatus()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openrouter",
            IsAvailable = true,
            PlanType = PlanType.Usage,
            RequestsAvailable = 100,
            UsedPercent = 25,
            Description = "75% remaining",
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal("75% remaining", presentation.StatusText);
    }

    [Fact]
    public void Create_UsesDescription_ForCurrencyUsagePlanStatus()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openrouter",
            IsAvailable = true,
            PlanType = PlanType.Usage,
            IsCurrencyUsage = true,
            RequestsAvailable = 100,
            RequestsUsed = 25,
            UsedPercent = 25,
            Description = "$75.00 remaining",
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal("$75.00 remaining", presentation.StatusText);
    }

    [Fact]
    public void Create_UsagePlanWithEmptyDescription_DoesNotFallbackToPercentText()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openrouter",
            IsAvailable = true,
            PlanType = PlanType.Usage,
            RequestsAvailable = 100,
            UsedPercent = 25,
            Description = string.Empty,
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal(string.Empty, presentation.StatusText);
    }

    [Fact]
    public void Create_FormatsDualQuotaBucketStatus_AndSuppressesSingleResetTime()
    {
        // Dual-bar data comes from WindowCards (flat ProviderUsage companion cards).
        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 4, // 4% used → 96% remaining
            NextResetTime = new DateTime(2026, 3, 7, 1, 0, 0),
            WindowCards = new[]
            {
                new ProviderUsage { ProviderId = "codex", Name = "5h",     WindowKind = WindowKind.Burst,   UsedPercent = 4.0 },
                new ProviderUsage { ProviderId = "codex", Name = "Weekly", WindowKind = WindowKind.Rolling, UsedPercent = 51.0 },
            },
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal("5h 96% remaining | Weekly 49% remaining", presentation.StatusText);
        Assert.True(presentation.SuppressSingleResetTime);
    }

    [Fact]
    public void Create_UsesDeclaredWindowLabels_ForKimiStyleLimitNames()
    {
        // Labels are driven by the window card's Name property.
        var usage = new ProviderUsage
        {
            ProviderId = "kimi-for-coding",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 25,
            WindowCards = new[]
            {
                new ProviderUsage { ProviderId = "kimi-for-coding", Name = "5h Limit",     WindowKind = WindowKind.Burst,   UsedPercent = 0.0 },
                new ProviderUsage { ProviderId = "kimi-for-coding", Name = "Weekly Limit", WindowKind = WindowKind.Rolling, UsedPercent = 25.0 },
            },
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        // Short window (5h/Burst) is top bar (Primary), long window (Weekly/Rolling) is bottom.
        Assert.Equal("5h Limit 0% used | Weekly Limit 25% used", presentation.StatusText);
    }

    // --- Pipeline regression tests ---
    // These tests verify the full pipeline from AgentGroupedUsageSnapshot through
    // GroupedUsageDisplayAdapter → ProviderCardPresentationCatalog so that bugs
    // suppressed by a broken intermediate layer cannot be masked.
    [Fact]
    public void Pipeline_KimiProviderDetailsWithoutModels_ProducesParentCard()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "kimi-for-coding",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 25,
                    Models = Array.Empty<AgentGroupedModelUsage>(),
                    ProviderDetails = new[]
                    {
                        new ProviderUsage
                        {
                            ProviderId = "kimi-for-coding",
                            Name = "5h Limit",
                            WindowKind = WindowKind.Burst,
                            UsedPercent = 0,
                        },
                        new ProviderUsage
                        {
                            ProviderId = "kimi-for-coding",
                            Name = "Weekly Limit",
                            WindowKind = WindowKind.Rolling,
                            UsedPercent = 25,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);
        var parent = Assert.Single(usages);
        Assert.NotNull(parent.WindowCards);
        Assert.Equal(2, parent.WindowCards!.Count);
    }

    [Fact]
    public void Pipeline_ClaudeCodeProviderDetailsWithoutModels_ProducesParentCard()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "claude-code",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 51,
                    Models = Array.Empty<AgentGroupedModelUsage>(),
                    ProviderDetails = new[]
                    {
                        new ProviderUsage
                        {
                            ProviderId = "claude-code",
                            Name = "Current Session",
                            WindowKind = WindowKind.Burst,
                            UsedPercent = 51,
                        },
                        new ProviderUsage
                        {
                            ProviderId = "claude-code",
                            Name = "All Models",
                            WindowKind = WindowKind.Rolling,
                            UsedPercent = 49,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);
        var parent = Assert.Single(usages);
        Assert.NotNull(parent.WindowCards);
        Assert.Equal(2, parent.WindowCards!.Count);
    }

    [Fact]
    public void Pipeline_ClaudeCodeProviderDetailsOnly_ProducesDualWindowStatus()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "claude-code",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 84,
                    Models = Array.Empty<AgentGroupedModelUsage>(),
                    ProviderDetails = new[]
                    {
                        new ProviderUsage
                        {
                            ProviderId = "claude-code",
                            Name = "Current Session",
                            WindowKind = WindowKind.Burst,
                            UsedPercent = 84,
                        },
                        new ProviderUsage
                        {
                            ProviderId = "claude-code",
                            Name = "All Models",
                            WindowKind = WindowKind.Rolling,
                            UsedPercent = 16,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);
        var usage = Assert.Single(usages);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);
        Assert.Equal("Current Session 16% remaining | All Models 84% remaining", presentation.StatusText);
    }

    [Theory]
    [InlineData("opencode-zen")]
    [InlineData("opencode-go")]
    public void Create_UsesCompactInlineStatus_ForOpenCodeProviders(string providerId)
    {
        var usage = new ProviderUsage
        {
            ProviderId = providerId,
            IsAvailable = true,
            PlanType = PlanType.Usage,
            IsCurrencyUsage = true,
            IsTooltipOnly = true,
            RequestsUsed = 12.34,
            Description = "$12.34 (4 sessions, 198 msgs, 7 days)",
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal("$12.34", presentation.StatusText);
    }

    [Fact]
    public void Create_ShowsWarningTone_ForHttp429Response()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = false,
            HttpStatus = 429,
            State = ProviderUsageState.Error,
            Description = "Rate limited — retry in 60 seconds",
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal(ProviderCardStatusTone.Warning, presentation.StatusTone);
        Assert.False(presentation.IsError, "HTTP 429 should not be treated as a generic error (would show red)");
        Assert.False(presentation.ShouldHaveProgress);
        Assert.Contains("Rate limited", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_ShowsFallbackRateLimitText_WhenDescriptionIsEmpty()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = false,
            HttpStatus = 429,
            State = ProviderUsageState.Error,
            Description = string.Empty,
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal(ProviderCardStatusTone.Warning, presentation.StatusTone);
        Assert.Contains("Rate limited", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_TreatsNon429HttpErrorsAsError()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = false,
            HttpStatus = 503,
            State = ProviderUsageState.Error,
            Description = "Service unavailable",
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal(ProviderCardStatusTone.Error, presentation.StatusTone);
        Assert.True(presentation.IsError);
    }

    [Fact]
    public void Create_ShowsWarningTone_ForExpiredSubscription()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "synthetic",
            IsAvailable = false,
            State = ProviderUsageState.Expired,
            Description = "No active subscription",
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal(ProviderCardStatusTone.Warning, presentation.StatusTone);
        Assert.True(presentation.IsExpired);
        Assert.False(presentation.IsError);
        Assert.False(presentation.IsMissing);
        Assert.False(presentation.ShouldHaveProgress);
        Assert.Equal("No active subscription", presentation.StatusText);
    }

    [Fact]
    public void Create_ShowsFallbackText_ForExpiredSubscriptionWithEmptyDescription()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "synthetic",
            IsAvailable = false,
            State = ProviderUsageState.Expired,
            Description = string.Empty,
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal(ProviderCardStatusTone.Warning, presentation.StatusTone);
        Assert.True(presentation.IsExpired);
        Assert.Equal("Subscription expired", presentation.StatusText);
    }
}
