// <copyright file="ProviderRefreshConfigSelectorTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
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
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
        };

        var selection = ProviderRefreshConfigSelector.SelectActiveConfigs(configs, forceAll: true, includeProviderIds: null);

        Assert.Single(selection.ActiveConfigs);
    }

    [Fact]
    public void SelectActiveConfigs_IncludesOnlyKeyedConfigs_WhenNotForceAll()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "codex", ApiKey = TestApiKey1 },
        };

        var selection = ProviderRefreshConfigSelector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }

    [Fact]
    public void SelectActiveConfigs_ExcludesNonPersistedProviders()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = TestApiKey1 },
            new() { ProviderId = "openai", ApiKey = TestApiKey2 },
        };

        var selection = ProviderRefreshConfigSelector.SelectActiveConfigs(configs, forceAll: false, includeProviderIds: null);

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }

    [Fact]
    public void SelectActiveConfigs_StandardApiKeyWithNoKey_IsExcludedEvenWhenForceAll()
    {
        // forceAll must not bypass the key requirement for StandardApiKey providers.
        // Polling them without a key can only return "API Key missing", which is useless
        // to store and causes stale "missing" rows to appear in the main window snapshot.
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "mistral" },   // StandardApiKey, no key
            new() { ProviderId = "codex" },     // SessionAuthStatus, no key — still included
        };

        var selection = ProviderRefreshConfigSelector.SelectActiveConfigs(configs, forceAll: true, includeProviderIds: null);

        Assert.DoesNotContain(selection.ActiveConfigs, c => string.Equals(c.ProviderId, "mistral", StringComparison.Ordinal));
        Assert.Contains(selection.ActiveConfigs, c => string.Equals(c.ProviderId, "codex", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectActiveConfigs_StandardApiKeyWithKey_IsIncludedWhenForceAll()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "mistral", ApiKey = TestApiKey1 },
        };

        var selection = ProviderRefreshConfigSelector.SelectActiveConfigs(configs, forceAll: true, includeProviderIds: null);

        Assert.Single(selection.ActiveConfigs);
        Assert.Equal("mistral", selection.ActiveConfigs[0].ProviderId);
    }

    [Fact]
    public void SelectActiveConfigs_FiltersToIncludedProviderIds()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = TestApiKey1 },
        };

        var selection = ProviderRefreshConfigSelector.SelectActiveConfigs(
            configs,
            forceAll: false,
            includeProviderIds: new[] { "codex" });

        var activeConfig = Assert.Single(selection.ActiveConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }
}
