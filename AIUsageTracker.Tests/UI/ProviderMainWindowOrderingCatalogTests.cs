// <copyright file="ProviderMainWindowOrderingCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderMainWindowOrderingCatalogTests
{
    [Fact]
    public void OrderForMainWindow_GroupsDerivedProvidersWithCanonicalFamily()
    {
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "github-copilot",
                ProviderName = "GitHub Copilot",
                IsQuotaBased = true,
                IsAvailable = true,
            },
            new()
            {
                ProviderId = "gemini-cli.primary",
                ProviderName = "Gemini 3 Flash Preview [Gemini CLI]",
                IsQuotaBased = true,
                IsAvailable = true,
            },
            new()
            {
                ProviderId = "gemini-cli",
                ProviderName = "Google Gemini",
                IsQuotaBased = true,
                IsAvailable = true,
            },
            new()
            {
                ProviderId = "gemini-cli.secondary",
                ProviderName = "Gemini 2.5 Pro [Gemini CLI]",
                IsQuotaBased = true,
                IsAvailable = true,
            },
        };

        var orderedProviderIds = ProviderMainWindowOrderingCatalog
            .OrderForMainWindow(usages)
            .Select(usage => usage.ProviderId)
            .ToList();

        Assert.Equal(
            new[] { "github-copilot", "gemini-cli.secondary", "gemini-cli.primary", "gemini-cli" },
            orderedProviderIds);
    }
}
