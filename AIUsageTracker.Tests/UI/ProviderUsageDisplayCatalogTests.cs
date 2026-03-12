// <copyright file="ProviderUsageDisplayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderUsageDisplayCatalogTests
{
    [Fact]
    public void PrepareForMainWindow_KeepsUnavailableAntigravityParentAndDeduplicatesProviders()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "openai", IsAvailable = true },
            new() { ProviderId = "openai", IsAvailable = false },
            new() { ProviderId = "antigravity", IsAvailable = false },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.DisplayableUsages.Count);
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "openai", StringComparison.Ordinal));
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "antigravity", StringComparison.Ordinal));
        Assert.True(preparation.HasAntigravityParent);
    }

    [Fact]
    public void PrepareForMainWindow_HidesAntigravityChildren_WhenParentExists()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "antigravity", IsAvailable = true },
            new() { ProviderId = "antigravity.gemini-pro", IsAvailable = true },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        var displayable = Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("antigravity", displayable.ProviderId);
    }

    [Fact]
    public void PrepareForMainWindow_KeptsCodexChildren_WhenParentExists()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "codex.spark", IsAvailable = true },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Equal(2, preparation.DisplayableUsages.Count);
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Contains(preparation.DisplayableUsages, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
    }

    [Fact]
    public void PrepareForMainWindow_HidesUnknownProviders_ButKeepsKnownProviders()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "legacy-unknown-provider", IsAvailable = true },
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("codex", preparation.DisplayableUsages[0].ProviderId);
    }

    [Fact]
    public void CreateAntigravityModelUsages_DeduplicatesAndBuildsSyntheticChildren()
    {
        var parent = new ProviderUsage
        {
            ProviderId = "antigravity",
            IsAvailable = true,
            AuthSource = "test",
            AccountName = "test.user@example.com",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Gemini Pro", Used = "75% remaining", NextResetTime = new DateTime(2026, 3, 7, 10, 0, 0) },
                new() { Name = "Gemini Pro", Used = "80% remaining" },
                new() { Name = "[internal]", Used = "10% remaining" },
                new() { Name = "Gemini Flash", Used = "55% remaining" },
                new() { Name = string.Empty, ModelName = "GPT OSS", Used = "100% remaining" },
            },
        };

        var children = ProviderUsageDisplayCatalog.CreateAntigravityModelUsages(parent);

        Assert.Equal(
            new[] { "antigravity.gemini-flash", "antigravity.gemini-pro", "antigravity.gpt-oss" },
            children.Select(child => child.ProviderId).ToArray());
        Assert.Contains(children, child => string.Equals(child.ProviderName, "Gemini Pro [Antigravity]", StringComparison.Ordinal));
        Assert.Contains(children, child => string.Equals(child.ProviderName, "GPT OSS [Antigravity]", StringComparison.Ordinal));
        Assert.All(children, child => Assert.Equal("test.user@example.com", child.AccountName));
        Assert.All(children, child => Assert.Equal(PlanType.Coding, child.PlanType));
        Assert.All(children, child => Assert.True(child.IsQuotaBased));
    }

    [Fact]
    public void PrepareForMainWindow_UsesCapabilitySnapshotPolicies_WhenProvided()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsAvailable = true },
            new() { ProviderId = "codex.spark", IsAvailable = true },
        };

        var capabilities = new AgentProviderCapabilitiesSnapshot
        {
            Providers =
            [
                new AgentProviderCapabilityDefinition
                {
                    ProviderId = "codex",
                    DisplayName = "OpenAI (Codex)",
                    SupportsChildProviderIds = true,
                    CollapseDerivedChildrenInMainWindow = true,
                    HandledProviderIds = ["codex", "codex.spark"],
                },
            ],
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages, capabilities);

        var displayable = Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("codex", displayable.ProviderId);
    }
}
