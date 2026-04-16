// <copyright file="ProviderSubDetailSectionCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSubDetailSectionCatalogTests
{
    // Build() and GetDisplayableDetails() were removed when ProviderUsageDetail was replaced
    // by flat ProviderUsage cards. The collapse/expand preference helpers remain.
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
}
