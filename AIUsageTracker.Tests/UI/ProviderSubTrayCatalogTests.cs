// <copyright file="ProviderSubTrayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

#pragma warning disable CS0618 // Used/UsedPercent: legacy fields set in test fixtures

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSubTrayCatalogTests
{
    [Fact]
    public void GetEligibleDetails_FiltersDeduplicatesAndSorts()
    {
        var usage = new ProviderUsage
        {
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Gemini 2.5 Pro", Description = "45% used", DetailType = ProviderUsageDetailType.Model },
                new() { Name = "Gemini 2.5 Flash", Description = "12% used", DetailType = ProviderUsageDetailType.Model },
                new() { Name = "Gemini 2.5 Pro", Description = "50% used", DetailType = ProviderUsageDetailType.Model },
                new() { Name = "internal-metric", Description = "10% used", DetailType = ProviderUsageDetailType.Unknown },
                new() { Name = "Credits", Description = "Unlimited", DetailType = ProviderUsageDetailType.Credit },
            },
        };

        var details = SettingsWindow.GetEligibleSubTrayDetails(usage);

        Assert.Equal(
            new[] { "Gemini 2.5 Flash", "Gemini 2.5 Pro" },
            details.Select(detail => detail.Name).ToArray());
    }

    [Fact]
    public void GetEligibleDetails_ReturnsEmpty_WhenUsageMissing()
    {
        var details = SettingsWindow.GetEligibleSubTrayDetails(null);

        Assert.Empty(details);
    }

    [Fact]
    public void GetEligibleDetails_IncludesModelEntriesWithoutPercentUsage()
    {
        var usage = new ProviderUsage
        {
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "GPT OSS", Description = "Unknown", DetailType = ProviderUsageDetailType.Model },
            },
        };

        var details = SettingsWindow.GetEligibleSubTrayDetails(usage);

        var detail = Assert.Single(details);
        Assert.Equal("GPT OSS", detail.Name);
    }

    [Fact]
    public void GetEligibleDetails_ReturnsEmpty_ForProvidersWithVisibleDerivedProviders()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "OpenAI (Codex)", DetailType = ProviderUsageDetailType.Model },
            },
        };

        var details = SettingsWindow.GetEligibleSubTrayDetails(usage);

        Assert.Empty(details);
    }
}
