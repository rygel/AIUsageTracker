// <copyright file="ProviderSubTrayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSubTrayCatalogTests
{
    [Fact]
    public void GetEligibleDetails_ReturnsEmpty_Always()
    {
        // Sub-tray details were removed when ProviderUsageDetail was replaced by flat ProviderUsage cards.
        var usage = new ProviderUsage { ProviderId = "gemini-cli" };

        var details = SettingsWindow.GetEligibleSubTrayDetails(usage);

        Assert.Empty(details);
    }

    [Fact]
    public void GetEligibleDetails_ReturnsEmpty_WhenUsageMissing()
    {
        var details = SettingsWindow.GetEligibleSubTrayDetails(null);

        Assert.Empty(details);
    }

    [Fact]
    public void GetEligibleDetails_ReturnsEmpty_ForProvidersWithVisibleDerivedProviders()
    {
        var usage = new ProviderUsage { ProviderId = "codex" };

        var details = SettingsWindow.GetEligibleSubTrayDetails(usage);

        Assert.Empty(details);
    }
}
