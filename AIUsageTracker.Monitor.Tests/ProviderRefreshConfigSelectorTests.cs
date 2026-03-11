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
            new() { ProviderId = "openai" },
            new() { ProviderId = "codex" },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: true, includeProviderIds: null);

        Assert.Equal(2, selection.ActiveConfigs.Count);
        Assert.Equal(0, selection.SuppressedConfigCount);
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
    public void SelectActiveConfigs_SuppressesSessionBackedAliasWhenCanonicalConfigExists()
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
        Assert.Equal(1, selection.SuppressedConfigCount);
    }

    [Fact]
    public void SelectActiveConfigs_DoesNotSuppressExplicitAliasApiKeyConfig()
    {
        var selector = CreateSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
            new() { ProviderId = "openai", ApiKey = "sk-live-openai" },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        Assert.Equal(2, selection.ActiveConfigs.Count);
        Assert.Equal(0, selection.SuppressedConfigCount);
    }

    [Fact]
    public void SelectActiveConfigs_FiltersToIncludedProviderIdsAfterSuppression()
    {
        var selector = CreateSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
            new() { ProviderId = "openai", ApiKey = "sk-live-openai" },
        };

        var selection = selector.SelectActiveConfigs(
            configs,
            forceAll: false,
            includeProviderIds: new[] { "openai" });

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("openai", activeConfig.ProviderId);
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
        bool supportsChildProviderIds = false)
    {
        var mock = new Mock<IProviderService>();
        mock.SetupGet(provider => provider.ProviderId).Returns(providerId);
        mock.SetupGet(provider => provider.Definition).Returns(
            new ProviderDefinition(
                providerId,
                displayName: providerId,
                planType: PlanType.Coding,
                isQuotaBased: true,
                defaultConfigType: "quota-based",
                autoIncludeWhenUnconfigured: autoIncludeWhenUnconfigured,
                supportsChildProviderIds: supportsChildProviderIds));
        mock.Setup(provider => provider.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(Array.Empty<ProviderUsage>());
        return mock.Object;
    }
}
