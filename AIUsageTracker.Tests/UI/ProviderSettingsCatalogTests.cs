using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSettingsCatalogTests
{
    [Fact]
    public void GetInputMode_ReturnsDerivedReadOnly_ForDerivedProviders()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var behavior = ProviderSettingsCatalog.Resolve(config, usage: null, isDerived: true);

        Assert.Equal(ProviderInputMode.DerivedReadOnly, behavior.InputMode);
    }

    [Fact]
    public void GetInputMode_ReturnsOpenAiSession_ForSessionToken()
    {
        var config = new ProviderConfig { ProviderId = "openai", ApiKey = "sess-token" };

        var behavior = ProviderSettingsCatalog.Resolve(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.OpenAiSessionStatus, behavior.InputMode);
    }

    [Fact]
    public void IsInactive_ReturnsFalse_ForDerivedProviders()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var behavior = ProviderSettingsCatalog.Resolve(config, usage: null, isDerived: true);

        Assert.False(behavior.IsInactive);
    }

    [Fact]
    public void IsDerivedProviderVisible_ReturnsTrue_ForCodexSpark()
    {
        Assert.True(ProviderSettingsCatalog.IsDerivedProviderVisible("codex.spark"));
    }

    [Fact]
    public void Resolve_ReturnsCodexSessionBehavior_ForCodex()
    {
        var config = new ProviderConfig { ProviderId = "codex", ApiKey = "sess-token" };

        var behavior = ProviderSettingsCatalog.Resolve(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.OpenAiSessionStatus, behavior.InputMode);
        Assert.Equal("OpenAI Codex", behavior.SessionProviderLabel);
        Assert.True(behavior.PreferCodexIdentity);
        Assert.False(behavior.IsInactive);
    }

    [Fact]
    public void Resolve_ReturnsOpenAiSessionBehavior_ForQuotaBasedOpenAi()
    {
        var config = new ProviderConfig { ProviderId = "openai", ApiKey = string.Empty };
        var usage = new ProviderUsage { ProviderId = "openai", IsQuotaBased = true, IsAvailable = true };

        var behavior = ProviderSettingsCatalog.Resolve(config, usage, isDerived: false);

        Assert.Equal(ProviderInputMode.OpenAiSessionStatus, behavior.InputMode);
        Assert.Equal("OpenAI", behavior.SessionProviderLabel);
        Assert.False(behavior.PreferCodexIdentity);
        Assert.False(behavior.IsInactive);
    }
}
