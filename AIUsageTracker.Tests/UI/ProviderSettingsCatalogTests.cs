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
    public void GetInputMode_ReturnsSessionAuth_ForCodexSpark()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.SessionAuthStatus, behavior.InputMode);
    }

    [Fact]
    public void GetInputMode_ReturnsSessionAuth_ForSessionToken()
    {
        var config = new ProviderConfig { ProviderId = "openai", ApiKey = TestApiKey };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.SessionAuthStatus, behavior.InputMode);
    }

    [Fact]
    public void IsInactive_ReturnsTrue_ForStandaloneCodexSparkWithoutAuth()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: false);

        Assert.True(behavior.IsInactive);
    }

    [Fact]
    public void Resolve_ReturnsStandaloneVisibility_ForCodexSpark()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: false);

        Assert.False(behavior.IsDerivedVisible);
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

    // ── Clear-key removal precondition ────────────────────────────────────────
    [Theory]
    [InlineData("deepseek")]
    [InlineData("mistral")]
    [InlineData("openrouter")]
    public void Resolve_ReturnsStandardApiKey_ForStandardProviderWithEmptyKey(string providerId)
    {
        // Verifies the precondition for clear-key removal: these providers must resolve
        // StandardApiKey mode so PersistAllSettingsAsync will call RemoveConfigAsync when
        // the user wipes the key field instead of saving an empty-key config.
        var config = new ProviderConfig { ProviderId = providerId, ApiKey = string.Empty };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.StandardApiKey, behavior.InputMode);
    }

    [Theory]
    [InlineData("deepseek", "sk-abc123")]
    [InlineData("mistral", "sk-abc123")]
    public void Resolve_ReturnsStandardApiKey_ForStandardProviderWithKey(string providerId, string apiKey)
    {
        // A configured standard provider must also resolve StandardApiKey so clearing
        // the field (empty string) triggers removal while a non-empty key gets saved.
        var config = new ProviderConfig { ProviderId = providerId, ApiKey = apiKey };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.StandardApiKey, behavior.InputMode);
    }
}
