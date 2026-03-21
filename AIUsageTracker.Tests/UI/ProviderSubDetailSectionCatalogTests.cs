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

        var section = ProviderSubDetailSectionCatalog.Build(usage, preferences);

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

        var section = Assert.IsType<ProviderSubDetailSection>(
            ProviderSubDetailSectionCatalog.Build(usage, preferences));

        Assert.Equal("github-copilot", section.ProviderId);
        Assert.Equal($"{ProviderMetadataCatalog.ResolveDisplayLabel(usage)} Details", section.Title);
        Assert.False(section.IsCollapsed);
        Assert.Single(section.Details);
    }

    [Fact]
    public void SetIsCollapsed_ProviderWithoutSharedPolicy_DoesNotMutateSharedPreference()
    {
        var preferences = new AppPreferences { IsAntigravityCollapsed = true };

        ProviderSubDetailSectionCatalog.SetIsCollapsed(preferences, "openai", isCollapsed: false);

        Assert.True(preferences.IsAntigravityCollapsed);
    }

    [Fact]
    public void GetIsCollapsed_NonSharedProvider_ReturnsFalse()
    {
        var preferences = new AppPreferences { IsAntigravityCollapsed = true };

        var collapsed = ProviderSubDetailSectionCatalog.GetIsCollapsed(preferences, "openai");

        Assert.False(collapsed);
    }
}
