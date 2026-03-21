// <copyright file="ProviderRenderPlanCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderRenderPlanCatalogTests
{
    [Fact]
    public void Build_EmptyInput_ReturnsNoDataMessage()
    {
        var plan = MainWindowRuntimeLogic.BuildProviderRenderPlan(
            Array.Empty<ProviderUsage>(),
            hiddenProviderItemIds: Array.Empty<string>());

        Assert.Equal(0, plan.RawCount);
        Assert.Equal(0, plan.RenderedCount);
        Assert.Equal("No provider data available.", plan.Message);
        Assert.Empty(plan.Sections);
    }

    [Fact]
    public void Build_UnknownProvidersOnly_ReturnsNoDisplayableMessage()
    {
        var usages = new[]
        {
            new ProviderUsage { ProviderId = "unknown-provider", ProviderName = "Unknown" },
        };

        var plan = MainWindowRuntimeLogic.BuildProviderRenderPlan(usages, hiddenProviderItemIds: Array.Empty<string>());

        Assert.Equal(1, plan.RawCount);
        Assert.Equal(0, plan.RenderedCount);
        Assert.Equal("Data received, but no displayable providers were found.", plan.Message);
        Assert.Empty(plan.Sections);
    }

    [Fact]
    public void Build_DisplayableProviders_ReturnsSectionLayouts()
    {
        var usages = new[]
        {
            new ProviderUsage { ProviderId = "gemini-cli", ProviderName = "Gemini CLI", IsQuotaBased = true },
            new ProviderUsage { ProviderId = "github-copilot", ProviderName = "GitHub Copilot", IsQuotaBased = false },
        };

        var plan = MainWindowRuntimeLogic.BuildProviderRenderPlan(usages, hiddenProviderItemIds: Array.Empty<string>());

        Assert.Equal(2, plan.RawCount);
        Assert.Null(plan.Message);
        Assert.True(plan.RenderedCount > 0);
        Assert.NotEmpty(plan.Sections);
        Assert.Equal(
            plan.RenderedCount,
            plan.Sections.SelectMany(section => section.Usages).Count());
    }
}
