// <copyright file="ProviderSectionCollapseCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSectionCollapseCatalogTests
{
    [Fact]
    public void GetIsCollapsed_QuotaSection_ReadsPlansAndQuotasFlag()
    {
        var preferences = new AppPreferences
        {
            IsPlansAndQuotasCollapsed = true,
            IsPayAsYouGoCollapsed = false,
        };

        var collapsed = ProviderSectionCollapseCatalog.GetIsCollapsed(preferences, isQuotaBased: true);

        Assert.True(collapsed);
    }

    [Fact]
    public void SetIsCollapsed_PayAsYouGoSection_OnlyUpdatesPayAsYouGoFlag()
    {
        var preferences = new AppPreferences
        {
            IsPlansAndQuotasCollapsed = true,
            IsPayAsYouGoCollapsed = false,
        };

        ProviderSectionCollapseCatalog.SetIsCollapsed(preferences, isQuotaBased: false, isCollapsed: true);

        Assert.True(preferences.IsPlansAndQuotasCollapsed);
        Assert.True(preferences.IsPayAsYouGoCollapsed);
    }
}
