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
            new() { ProviderId = "codex" },
        };

        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex.spark", IsQuotaBased = true, PlanType = PlanType.Coding },
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
            new() { ProviderId = "codex.spark" },
        };

        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex.spark", IsQuotaBased = true },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, usages);

        Assert.Single(items, item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.False(items.Single(item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal)).IsDerived);
    }

    [Fact]
    public void CreateDisplayItems_SortsByDisplayNameThenProviderId()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "xiaomi" },
            new() { ProviderId = "codex" },
            new() { ProviderId = "opencode-zen" },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.Equal(
            new[] { "codex", "opencode-zen", "xiaomi" },
            items.Where(item => new[] { "codex", "opencode-zen", "xiaomi" }.Contains(item.Config.ProviderId, StringComparer.Ordinal))
                .Select(item => item.Config.ProviderId)
                .ToArray());
    }

    [Fact]
    public void CreateDisplayItems_IncludesSupportedProviders_WhenNoConfigsExist()
    {
        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(Array.Empty<ProviderConfig>(), Array.Empty<ProviderUsage>());

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "openai", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal) && !item.IsDerived);
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "opencode-zen", StringComparison.Ordinal) && !item.IsDerived);
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "minimax", StringComparison.Ordinal) && !item.IsDerived);
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "minimax-io", StringComparison.Ordinal) && !item.IsDerived);
    }

    [Fact]
    public void CreateDisplayItems_HidesLegacyOpenAiConfigFromSettingsList()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "codex" },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "openai", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_HidesLegacyAnthropicConfigFromSettingsList()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "anthropic" },
            new() { ProviderId = "claude-code" },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "anthropic", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "claude-code", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_GroupsDerivedCodexSpark_UnderCodex()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
            new() { ProviderId = "deepseek" },
        };

        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex.spark", IsQuotaBased = true, PlanType = PlanType.Coding },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, usages);
        var codexIndex = items
            .Select((item, index) => new { item, index })
            .Where(entry => string.Equals(entry.item.Config.ProviderId, "codex", StringComparison.Ordinal))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        var sparkIndex = items
            .Select((item, index) => new { item, index })
            .Where(entry => string.Equals(entry.item.Config.ProviderId, "codex.spark", StringComparison.Ordinal))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();

        Assert.True(codexIndex >= 0);
        Assert.Equal(codexIndex + 1, sparkIndex);
        Assert.True(items[sparkIndex].IsDerived);
    }
}
