// <copyright file="ProviderSettingsCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSettingsCatalogTests
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();
    [Fact]
    public void GetInputMode_ReturnsDerivedReadOnly_ForDerivedProviders()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: true);

        Assert.Equal(ProviderInputMode.DerivedReadOnly, behavior.InputMode);
    }

    [Fact]
    public void GetInputMode_ReturnsSessionAuth_ForSessionToken()
    {
        var config = new ProviderConfig { ProviderId = "openai", ApiKey = TestApiKey };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.SessionAuthStatus, behavior.InputMode);
    }

    [Fact]
    public void IsInactive_ReturnsFalse_ForDerivedProviders()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: true);

        Assert.False(behavior.IsInactive);
    }

    [Fact]
    public void Resolve_ReturnsDerivedVisible_ForCodexSpark()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: true);

        Assert.True(behavior.IsDerivedVisible);
    }

    [Fact]
    public void Resolve_ReturnsCodexSessionBehavior_ForCodex()
    {
        var config = new ProviderConfig { ProviderId = "codex", ApiKey = TestApiKey };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.SessionAuthStatus, behavior.InputMode);
        Assert.Equal("OpenAI (Codex)", behavior.SessionProviderLabel);
        Assert.False(behavior.IsInactive);
    }

    [Fact]
    public void Resolve_ReturnsOpenAiSessionBehavior_ForQuotaBasedOpenAi()
    {
        var config = new ProviderConfig { ProviderId = "openai", ApiKey = string.Empty };
        var usage = new ProviderUsage { ProviderId = "openai", IsQuotaBased = true, IsAvailable = true };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage, isDerived: false);

        Assert.Equal(ProviderInputMode.SessionAuthStatus, behavior.InputMode);
        Assert.Equal("OpenAI (API)", behavior.SessionProviderLabel);
        Assert.False(behavior.IsInactive);
    }
}
