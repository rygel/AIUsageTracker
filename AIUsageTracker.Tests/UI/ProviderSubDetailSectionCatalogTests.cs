// <copyright file="ProviderSubDetailSectionCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSubDetailSectionCatalogTests
{
    [Fact]
    public void Build_ReturnsNull_WhenNoDisplayableDetails()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "gemini-cli",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Requests / Day", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Rolling },
            },
        };
        var preferences = new AppPreferences { IsAntigravityCollapsed = true };

        var section = MainWindowRuntimeLogic.Build(usage, preferences);

        Assert.Null(section);
    }

    [Fact]
    public void Build_DisplayableProvider_ReturnsSectionWithResolvedTitle()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "github-copilot",
            ProviderName = "GitHub Copilot",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Requests", DetailType = ProviderUsageDetailType.Model },
            },
        };
        var preferences = new AppPreferences { IsAntigravityCollapsed = true };

        var sectionOpt = MainWindowRuntimeLogic.Build(usage, preferences);
        Assert.NotNull(sectionOpt);
        var section = sectionOpt!.Value;

        Assert.Equal("github-copilot", section.ProviderId);
        Assert.Equal($"{ProviderMetadataCatalog.ResolveDisplayLabel(usage)} Details", section.Title);
        Assert.False(section.IsCollapsed);
        Assert.Single(section.Details);
    }

    [Fact]
    public void SetIsCollapsed_ProviderWithoutSharedPolicy_DoesNotMutateSharedPreference()
    {
        var preferences = new AppPreferences { IsAntigravityCollapsed = true };

        MainWindowRuntimeLogic.SetIsCollapsed(preferences, "openai", isCollapsed: false);

        Assert.True(preferences.IsAntigravityCollapsed);
    }

    [Fact]
    public void GetIsCollapsed_NonSharedProvider_ReturnsFalse()
    {
        var preferences = new AppPreferences { IsAntigravityCollapsed = true };

        var collapsed = MainWindowRuntimeLogic.GetIsCollapsed(preferences, "openai");

        Assert.False(collapsed);
    }

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

        var details = MainWindowRuntimeLogic.GetDisplayableDetails(usage);

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

        var details = MainWindowRuntimeLogic.GetDisplayableDetails(usage);

        Assert.Empty(details);
    }

    [Fact]
    public void BuildDetailPresentation_UsesPercentDisplay_WhenPercentAvailable()
    {
        var detail = new ProviderUsageDetail
        {
            NextResetTime = new DateTime(2026, 3, 7, 9, 0, 0),
        };
        detail.SetPercentageValue(35.0, PercentageValueSemantic.Used);

        var presentation = MainWindowRuntimeLogic.BuildDetailPresentation(
            detail,
            showUsed: true,
            _ => "2h 0m");

        Assert.True(presentation.HasProgress);
        Assert.Equal(35, presentation.UsedPercent);
        Assert.Equal(35, presentation.IndicatorWidth);
        Assert.Equal("35%", presentation.DisplayText);
        Assert.Equal("(2h 0m)", presentation.ResetText);
    }

    [Fact]
    public void BuildDetailPresentation_FallsBackToRawValue_WhenPercentUnavailable()
    {
        var detail = new ProviderUsageDetail
        {
            Description = "Unlimited",
        };

        var presentation = MainWindowRuntimeLogic.BuildDetailPresentation(
            detail,
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

        var details = MainWindowRuntimeLogic.GetDisplayableDetails(usage);

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
                new() { Name = "Requests / Day", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Rolling },
                new() { Name = "Requests / Minute", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Burst },
                new() { Name = "Ignored Credit", DetailType = ProviderUsageDetailType.Credit },
            },
        };

        var details = MainWindowRuntimeLogic.GetDisplayableDetails(usage);

        Assert.Empty(details);
    }
}


