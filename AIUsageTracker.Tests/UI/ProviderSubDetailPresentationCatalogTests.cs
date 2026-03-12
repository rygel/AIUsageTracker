// <copyright file="ProviderSubDetailPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSubDetailPresentationCatalogTests
{
    [Fact]
    public void GetDisplayableDetails_FiltersAndSorts()
    {
        var usage = new ProviderUsage
        {
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Beta", DetailType = ProviderUsageDetailType.Other },
                new() { Name = "Alpha", DetailType = ProviderUsageDetailType.Model },
                new() { Name = "Ignored", DetailType = ProviderUsageDetailType.Credit },
                new() { Name = string.Empty, DetailType = ProviderUsageDetailType.Model },
            },
        };

        var details = ProviderSubDetailPresentationCatalog.GetDisplayableDetails(usage);

        Assert.Equal(new[] { "Alpha", "Beta" }, details.Select(detail => detail.Name).ToArray());
    }

    [Fact]
    public void GetDisplayableDetails_ReturnsEmpty_ForProvidersWithVisibleDerivedProviders()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "OpenAI (Codex)", DetailType = ProviderUsageDetailType.Model },
            },
        };

        var details = ProviderSubDetailPresentationCatalog.GetDisplayableDetails(usage);

        Assert.Empty(details);
    }

    [Fact]
    public void Create_UsesPercentDisplay_WhenPercentAvailable()
    {
        var detail = new ProviderUsageDetail
        {
            Used = "35% used",
            NextResetTime = new DateTime(2026, 3, 7, 9, 0, 0),
        };

        var presentation = ProviderSubDetailPresentationCatalog.Create(
            detail,
            isQuotaBased: false,
            showUsed: true,
            _ => "2h 0m");

        Assert.True(presentation.HasProgress);
        Assert.Equal(35, presentation.UsedPercent);
        Assert.Equal(35, presentation.IndicatorWidth);
        Assert.Equal("35%", presentation.DisplayText);
        Assert.Equal("(2h 0m)", presentation.ResetText);
    }

    [Fact]
    public void Create_FallsBackToRawValue_WhenPercentUnavailable()
    {
        var detail = new ProviderUsageDetail
        {
            Used = "Unlimited",
        };

        var presentation = ProviderSubDetailPresentationCatalog.Create(
            detail,
            isQuotaBased: false,
            showUsed: false,
            _ => "ignored");

        Assert.False(presentation.HasProgress);
        Assert.Equal("Unlimited", presentation.DisplayText);
        Assert.Null(presentation.ResetText);
    }

    [Theory]
    [InlineData("opencode-zen")]
    [InlineData("opencode-go")]
    public void GetDisplayableDetails_ReturnsEmpty_ForTooltipOnlyOpenCodeProviders(string providerId)
    {
        var usage = new ProviderUsage
        {
            ProviderId = providerId,
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Sessions", DetailType = ProviderUsageDetailType.Other, Description = "4 sessions" },
                new() { Name = "Messages", DetailType = ProviderUsageDetailType.Other, Description = "198 messages" },
            },
        };

        var details = ProviderSubDetailPresentationCatalog.GetDisplayableDetails(usage);

        Assert.Empty(details);
    }

    [Fact]
    public void GetDisplayableDetails_DoesNotIncludeGeminiQuotaWindows_AsSubItems()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "gemini-cli",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Requests / Day", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Secondary },
                new() { Name = "Requests / Minute", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Primary },
                new() { Name = "Ignored Credit", DetailType = ProviderUsageDetailType.Credit },
            },
        };

        var details = ProviderSubDetailPresentationCatalog.GetDisplayableDetails(usage);

        Assert.Empty(details);
    }
}
