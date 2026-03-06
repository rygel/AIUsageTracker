using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSettingsDisplayCatalogTests
{
    [Fact]
    public void CreateDisplayItems_AddsDerivedProviders_NotAlreadyConfigured()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" }
        };

        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex.spark", IsQuotaBased = true, PlanType = PlanType.Coding }
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, usages);

        var derived = Assert.Single(items, item => item.IsDerived);
        Assert.Equal("codex.spark", derived.Config.ProviderId);
        Assert.Equal("quota-based", derived.Config.Type);
        Assert.Equal(PlanType.Coding, derived.Config.PlanType);
    }

    [Fact]
    public void CreateDisplayItems_DoesNotDuplicateAlreadyConfiguredDerivedProvider()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex.spark" }
        };

        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex.spark", IsQuotaBased = true }
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, usages);

        Assert.Single(items);
        Assert.False(items[0].IsDerived);
    }

    [Fact]
    public void CreateDisplayItems_SortsByDisplayNameThenProviderId()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "xiaomi" },
            new() { ProviderId = "openai" },
            new() { ProviderId = "opencode" }
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.Equal(
            new[] { "openai", "opencode", "xiaomi" },
            items.Select(item => item.Config.ProviderId).ToArray());
    }
}
