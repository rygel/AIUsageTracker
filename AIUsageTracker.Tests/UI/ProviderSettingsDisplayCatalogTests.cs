// <copyright file="ProviderSettingsDisplayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
            new() { ProviderId = "openai" },
        };

        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex.spark", IsQuotaBased = true, PlanType = PlanType.Coding },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, usages);

        var derived = Assert.Single(items.Where(item => item.IsDerived));
        Assert.Equal("codex.spark", derived.Config.ProviderId);
        Assert.Equal("quota-based", derived.Config.Type);
        Assert.Equal(PlanType.Coding, derived.Config.PlanType);
    }

    [Fact]
    public void CreateDisplayItems_DoesNotDuplicateAlreadyConfiguredDerivedProvider()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex.spark" },
        };

        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex.spark", IsQuotaBased = true },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, usages);

        Assert.Single(items.Where(item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal)));
        Assert.False(items.Single(item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal)).IsDerived);
    }

    [Fact]
    public void CreateDisplayItems_SortsByDisplayNameThenProviderId()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "xiaomi" },
            new() { ProviderId = "openai" },
            new() { ProviderId = "opencode" },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.Equal(
            new[] { "openai", "opencode", "xiaomi" },
            items.Where(item => new[] { "openai", "opencode", "xiaomi" }.Contains(item.Config.ProviderId, StringComparer.Ordinal))
                .Select(item => item.Config.ProviderId)
                .ToArray());
    }

    [Fact]
    public void CreateDisplayItems_IncludesSupportedProviders_WhenNoConfigsExist()
    {
        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(Array.Empty<ProviderConfig>(), Array.Empty<ProviderUsage>());

        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "openai", StringComparison.Ordinal) && !item.IsDerived);
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "opencode", StringComparison.Ordinal) && !item.IsDerived);
    }
}
