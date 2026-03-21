// <copyright file="UpdateNotificationPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class UpdateNotificationPresentationCatalogTests
{
    [Fact]
    public void Create_WithUpdateAndControls_ShowsBannerAndSetsText()
    {
        var presentation = UpdateNotificationPresentationCatalog.Create(
            latestVersion: "2.4.1",
            hasBanner: true,
            hasText: true);

        Assert.True(presentation.ApplyBannerVisibility);
        Assert.True(presentation.ShowBanner);
        Assert.True(presentation.ApplyBannerText);
        Assert.Equal("New version available: 2.4.1", presentation.BannerText);
    }

    [Fact]
    public void Create_WithUpdateButMissingText_DoesNotApplyBannerChanges()
    {
        var presentation = UpdateNotificationPresentationCatalog.Create(
            latestVersion: "2.4.1",
            hasBanner: true,
            hasText: false);

        Assert.False(presentation.ApplyBannerVisibility);
        Assert.False(presentation.ApplyBannerText);
    }

    [Fact]
    public void Create_WithoutUpdateAndWithBanner_HidesBanner()
    {
        var presentation = UpdateNotificationPresentationCatalog.Create(
            latestVersion: null,
            hasBanner: true,
            hasText: true);

        Assert.True(presentation.ApplyBannerVisibility);
        Assert.False(presentation.ShowBanner);
        Assert.False(presentation.ApplyBannerText);
    }

    [Fact]
    public void Create_WithoutUpdateAndWithoutBanner_NoOp()
    {
        var presentation = UpdateNotificationPresentationCatalog.Create(
            latestVersion: null,
            hasBanner: false,
            hasText: true);

        Assert.False(presentation.ApplyBannerVisibility);
        Assert.False(presentation.ApplyBannerText);
    }
}
