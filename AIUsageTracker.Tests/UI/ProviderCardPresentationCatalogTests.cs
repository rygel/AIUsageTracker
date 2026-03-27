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
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal("75% remaining", presentation.StatusText);
    }

    [Fact]
    public void Create_FormatsDualQuotaBucketStatus_AndSuppressesSingleResetTime()
    {
        var burstDetail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            Description = "96% remaining (4% used)",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        burstDetail.SetPercentageValue(4.0, PercentageValueSemantic.Used); // 4% used

        var rollingDetail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            Description = "49% remaining (51% used)",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        rollingDetail.SetPercentageValue(51.0, PercentageValueSemantic.Used); // 51% used

        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 4, // 4% used → 96% remaining
            NextResetTime = new DateTime(2026, 3, 7, 1, 0, 0),
            Details = new List<ProviderUsageDetail> { burstDetail, rollingDetail },
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Equal("5h 96% remaining | Weekly 49% remaining", presentation.StatusText);
        Assert.True(presentation.SuppressSingleResetTime);
    }

    [Fact]
    public void Create_UsesDeclaredWindowLabels_ForKimiStyleLimitNames()
    {
        // Labels are driven by provider-declared quota windows.
        // Kimi declares "5h Limit" -> "5h" and "Weekly Limit" -> "Weekly".
        var burstDetail = new ProviderUsageDetail
        {
            Name = "5h Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        burstDetail.SetPercentageValue(0.0, PercentageValueSemantic.Used);

        var rollingDetail = new ProviderUsageDetail
        {
            Name = "Weekly Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        rollingDetail.SetPercentageValue(25.0, PercentageValueSemantic.Used);

        var usage = new ProviderUsage
        {
            ProviderId = "kimi-for-coding",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 25,
            Details = new List<ProviderUsageDetail> { rollingDetail, burstDetail },
        };

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        // Short window (5h/Burst) is top bar (Primary), long window (Weekly/Rolling) is bottom.
        Assert.Equal("5h 0% used | Weekly 25% used", presentation.StatusText);
    }

    // --- Pipeline regression tests ---
    // These tests verify the full pipeline from AgentGroupedUsageSnapshot through
    // GroupedUsageDisplayAdapter → ProviderCardPresentationCatalog so that bugs
    // suppressed by a broken intermediate layer cannot be masked.
    [Fact]
    public void Pipeline_KimiProviderDetails_ProducesDualBarOnParentCard()
    {
        // Regression: Kimi has no Model-type details, only QuotaWindow.
        // Before the fix, ProviderDetails was never carried through the pipeline,
        // parentUsage.Details was null, and TryGetPresentation returned false — no dual bar.
        var weeklyDetail = new ProviderUsageDetail
        {
            Name = "Weekly Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        weeklyDetail.SetPercentageValue(25.0, PercentageValueSemantic.Used, decimalPlaces: 1);

        var burstDetail = new ProviderUsageDetail
        {
            Name = "5h Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        burstDetail.SetPercentageValue(0.0, PercentageValueSemantic.Used, decimalPlaces: 1);

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
                    ProviderDetails = new[] { weeklyDetail, burstDetail },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);
        var parent = Assert.Single(usages, u => string.Equals(u.ProviderId, "kimi-for-coding", StringComparison.Ordinal));

        var presentation = MainWindowRuntimeLogic.Create(parent, showUsed: false);

        Assert.True(presentation.HasDualBuckets, "Kimi parent card must render dual progress bars");
        Assert.True(presentation.ShouldHaveProgress);
        Assert.Equal(0, presentation.DualBucketPrimaryUsed!.Value, precision: 0);   // 5h (Burst) top bar
        Assert.Equal(25, presentation.DualBucketSecondaryUsed!.Value, precision: 0); // Weekly (Rolling) bottom bar
        Assert.Contains("Weekly", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5h", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pipeline_ClaudeCode_ProducesDualBarOnParentCard()
    {
        // Verifies the full snapshot → Expand → Create() path for claude-code.
        // ParseOAuthUsageResponse uses "Current Session" (Burst) and "All Models" (Rolling).
        var sessionDetail = new ProviderUsageDetail
        {
            Name = "Current Session",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        sessionDetail.SetPercentageValue(4.0, PercentageValueSemantic.Used, decimalPlaces: 0);

        var allModelsDetail = new ProviderUsageDetail
        {
            Name = "All Models",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        allModelsDetail.SetPercentageValue(51.0, PercentageValueSemantic.Used, decimalPlaces: 0);

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
                    ProviderDetails = new[] { sessionDetail, allModelsDetail },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);
        var parent = Assert.Single(usages, u => string.Equals(u.ProviderId, "claude-code", StringComparison.Ordinal));

        var presentation = MainWindowRuntimeLogic.Create(parent, showUsed: false);

        Assert.True(presentation.HasDualBuckets, "claude-code must render dual bars: 5h (Burst) + 7-day (Rolling)");
        Assert.True(presentation.ShouldHaveProgress);
        Assert.Equal(4.0, presentation.DualBucketPrimaryUsed!.Value, precision: 1);   // Current Session (Burst)
        Assert.Equal(51.0, presentation.DualBucketSecondaryUsed!.Value, precision: 1); // All Models (Rolling)
        Assert.Equal("5h", presentation.DualBucketPrimaryLabel);
        Assert.Equal("7-day", presentation.DualBucketSecondaryLabel);
    }

    [Fact]
    public void Pipeline_ClaudeCode_WithSonnetModel_ShowsDualBarsAndSonnetDetailRow()
    {
        // Verifies that ProviderDetails (the single source of truth) carries QuotaWindow entries
        // for dual bars AND a Model entry for Sonnet, all flowing directly to the parent card.
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
                    ProviderDetails = new ProviderUsageDetail[]
                    {
                        new() { Name = "Current Session", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Burst, PercentageValue = 14, PercentageSemantic = PercentageValueSemantic.Used },
                        new() { Name = "All Models",      DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Rolling, PercentageValue = 84, PercentageSemantic = PercentageValueSemantic.Used },
                        new() { Name = "Sonnet",          DetailType = ProviderUsageDetailType.Model,       QuotaBucketKind = WindowKind.ModelSpecific, PercentageValue = 8, PercentageSemantic = PercentageValueSemantic.Used },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);
        var parent = Assert.Single(usages, u => string.Equals(u.ProviderId, "claude-code", StringComparison.Ordinal));

        // Dual bars come from the two QuotaWindow entries
        var presentation = MainWindowRuntimeLogic.Create(parent, showUsed: false);
        Assert.True(presentation.HasDualBuckets, "Dual bars must render from ProviderDetails QuotaWindow entries");
        Assert.Equal("5h", presentation.DualBucketPrimaryLabel);
        Assert.Equal("7-day", presentation.DualBucketSecondaryLabel);

        // Sonnet appears as a Model-type detail row
        Assert.NotNull(parent.Details);
        var sonnetDetail = parent.Details!.FirstOrDefault(d =>
            string.Equals(d.Name, "Sonnet", StringComparison.OrdinalIgnoreCase) &&
            d.DetailType == ProviderUsageDetailType.Model);
        Assert.NotNull(sonnetDetail);
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
}

