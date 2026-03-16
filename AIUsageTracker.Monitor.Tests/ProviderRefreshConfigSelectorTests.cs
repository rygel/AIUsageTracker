// <copyright file="ProviderRefreshConfigSelectorTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshConfigSelectorTests
{
    [Fact]
    public void EnsureAutoIncludedConfigs_AddsDefaultConfigForAutoIncludedProvider()
    {
        var selector = CreateSelector(CreateProvider("antigravity", autoIncludeWhenUnconfigured: true));
        var configs = new List<ProviderConfig>();

        selector.EnsureAutoIncludedConfigs(configs);

        var config = Assert.Single(configs);
        Assert.Equal("antigravity", config.ProviderId);
    }

    [Fact]
    public void EnsureAutoIncludedConfigs_DoesNotDuplicateExistingConfig()
    {
        var selector = CreateSelector(CreateProvider("antigravity", autoIncludeWhenUnconfigured: true));
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "antigravity" },
        };

        selector.EnsureAutoIncludedConfigs(configs);

        Assert.Single(configs);
    }

    [Fact]
    public void SelectActiveConfigs_ReturnsAllConfigs_WhenForceAllIsTrue()
    {
        var selector = CreateSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: true, includeProviderIds: null);

        Assert.Single(selection.ActiveConfigs);
    }

    [Fact]
    public void PrepareConfigs_IncludesAutoIncludedProvidersAndKeyedConfigsOnly()
    {
        var selector = CreateSelector(CreateProvider("antigravity", autoIncludeWhenUnconfigured: true));
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "codex", ApiKey = "codex-session" },
        };

        selector.EnsureAutoIncludedConfigs(configs);
        var selection = selector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        Assert.Equal(
            new[] { "antigravity", "codex" },
            selection.ActiveConfigs.Select(config => config.ProviderId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void SelectActiveConfigs_IncludesChildProviderIdsForAutoIncludedFamilies()
    {
        var selector = CreateSelector(
            CreateProvider(
                "antigravity",
                autoIncludeWhenUnconfigured: true,
                familyMode: ProviderFamilyMode.DynamicChildProviderRows));
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "antigravity.claude-4-sonnet" },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("antigravity.claude-4-sonnet", activeConfig.ProviderId);
    }

    [Fact]
    public void SelectActiveConfigs_ExcludesNonPersistedProviders()
    {
        var selector = CreateSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
            new() { ProviderId = "openai", ApiKey = "legacy-session-token" },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }

    [Fact]
    public void SelectActiveConfigs_DoesNotAlterPersistedProviderSet()
    {
        var selector = CreateSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        Assert.Single(selection.ActiveConfigs);
    }

    [Fact]
    public void SelectActiveConfigs_FiltersToIncludedProviderIdsAfterSuppression()
    {
        var selector = CreateSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
        };

        var selection = selector.SelectActiveConfigs(
            configs,
            forceAll: false,
            includeProviderIds: new[] { "codex" });

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }

    private static ProviderRefreshConfigSelector CreateSelector(params IProviderService[] providers)
    {
        return new ProviderRefreshConfigSelector(
            providers,
            NullLogger<ProviderRefreshConfigSelector>.Instance);
    }

    private static IProviderService CreateProvider(
        string providerId,
        bool autoIncludeWhenUnconfigured = false,
        ProviderFamilyMode familyMode = ProviderFamilyMode.Standalone)
    {
        var mock = new Mock<IProviderService>();
        mock.SetupGet(provider => provider.ProviderId).Returns(providerId);
        mock.SetupGet(provider => provider.Definition).Returns(
            new ProviderDefinition(
                providerId,
                providerId,
                PlanType.Coding,
                isQuotaBased: true,
                defaultConfigType: "quota-based")
            {
                AutoIncludeWhenUnconfigured = autoIncludeWhenUnconfigured,
                FamilyMode = familyMode,
            });
        mock.Setup(provider => provider.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(Array.Empty<ProviderUsage>());
        return mock.Object;
    }
}
