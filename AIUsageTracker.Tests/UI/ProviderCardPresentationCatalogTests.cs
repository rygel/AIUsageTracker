// <copyright file="ProviderCardPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
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

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

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

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

        Assert.Equal("100% remaining", presentation.StatusText);
        Assert.True(presentation.ShouldHaveProgress);
    }

    [Fact]
    public void Create_ShowsProgress_ForSyntheticAggregateChildCard()
    {
        // claude-code.current-session is a synthetic child whose canonical ID resolves to
        // "claude-code" (an aggregate parent). It must NOT be treated as an aggregate parent
        // itself — otherwise shouldHaveProgress would always be false and no bar would render.
        var usage = new ProviderUsage
        {
            ProviderId = "claude-code.current-session",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 35, // 35% used → 65% remaining
            Description = "65% Remaining",
        };

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

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

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

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

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: true);

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

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

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

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

        Assert.Equal("5h 96% remaining | Weekly 49% remaining", presentation.StatusText);
        Assert.True(presentation.SuppressSingleResetTime);
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

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

        Assert.Equal("$12.34", presentation.StatusText);
    }
}
