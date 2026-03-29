// <copyright file="ProviderRefreshConfigSelectorTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshConfigSelectorTests
{
    private static readonly string TestApiKey1 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey2 = Guid.NewGuid().ToString();

    [Fact]
    public void SelectActiveConfigs_ReturnsAllConfigs_WhenForceAllIsTrue()
    {
        var selector = new ProviderRefreshConfigSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: true, includeProviderIds: null);

        Assert.Single(selection.ActiveConfigs);
    }

    [Fact]
    public void SelectActiveConfigs_IncludesOnlyKeyedConfigs_WhenNotForceAll()
    {
        var selector = new ProviderRefreshConfigSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "codex", ApiKey = TestApiKey1 },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }

    [Fact]
    public void SelectActiveConfigs_ExcludesNonPersistedProviders()
    {
        var selector = new ProviderRefreshConfigSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = TestApiKey1 },
            new() { ProviderId = "openai", ApiKey = TestApiKey2 },
        };

        var selection = selector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }

    [Fact]
    public void SelectActiveConfigs_FiltersToIncludedProviderIds()
    {
        var selector = new ProviderRefreshConfigSelector();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = TestApiKey1 },
        };

        var selection = selector.SelectActiveConfigs(
            configs,
            forceAll: false,
            includeProviderIds: new[] { "codex" });

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }
}
