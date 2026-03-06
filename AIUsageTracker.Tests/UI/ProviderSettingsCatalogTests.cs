using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSettingsCatalogTests
{
    [Fact]
    public void GetInputMode_ReturnsDerivedReadOnly_ForDerivedProviders()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var mode = ProviderSettingsCatalog.GetInputMode(config, usage: null, isDerived: true);

        Assert.Equal(ProviderInputMode.DerivedReadOnly, mode);
    }

    [Fact]
    public void GetInputMode_ReturnsOpenAiSession_ForSessionToken()
    {
        var config = new ProviderConfig { ProviderId = "openai", ApiKey = "sess-token" };

        var mode = ProviderSettingsCatalog.GetInputMode(config, usage: null, isDerived: false);

        Assert.Equal(ProviderInputMode.OpenAiSessionStatus, mode);
    }

    [Fact]
    public void IsInactive_ReturnsFalse_ForDerivedProviders()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark", ApiKey = string.Empty };

        var isInactive = ProviderSettingsCatalog.IsInactive(config, usage: null, isDerived: true, ProviderInputMode.DerivedReadOnly);

        Assert.False(isInactive);
    }

    [Fact]
    public void IsDerivedProviderVisible_ReturnsTrue_ForCodexSpark()
    {
        Assert.True(ProviderSettingsCatalog.IsDerivedProviderVisible("codex.spark"));
    }
}
