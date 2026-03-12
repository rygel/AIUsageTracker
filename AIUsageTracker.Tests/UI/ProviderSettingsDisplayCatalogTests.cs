// <copyright file="ProviderSettingsDisplayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSettingsDisplayCatalogTests
{
    [Fact]
    public void CreateDisplayItems_IncludesCatalogProviders_NotAlreadyConfigured()
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

        var spark = Assert.Single(items, item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.False(spark.IsDerived);
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
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal) && !item.IsDerived);
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
    public void CreateDisplayItems_HidesUnknownConfiguredProviders_FromSettingsList()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "unknown-provider" },
            new() { ProviderId = "codex" },
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, Array.Empty<ProviderUsage>());

        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "unknown-provider", StringComparison.Ordinal));
        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDisplayItems_SortsAlphabeticallyByDisplayName()
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
        var orderedIds = items
            .Where(item =>
                string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal) ||
                string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal) ||
                string.Equals(item.Config.ProviderId, "deepseek", StringComparison.Ordinal))
            .Select(item => item.Config.ProviderId)
            .ToArray();

        Assert.Equal(new[] { "deepseek", "codex", "codex.spark" }, orderedIds);
    }

    [Fact]
    public void CreateDisplayItems_UsesCapabilitySnapshotToHideProviderInSettings()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
            new() { ProviderId = "codex.spark" },
        };

        var capabilities = new AgentProviderCapabilitiesSnapshot
        {
            Providers =
            [
                new AgentProviderCapabilityDefinition
                {
                    ProviderId = "codex",
                    DisplayName = "OpenAI (Codex)",
                    SupportsChildProviderIds = false,
                    ShowInSettings = true,
                    HandledProviderIds = ["codex"],
                },
                new AgentProviderCapabilityDefinition
                {
                    ProviderId = "codex.spark",
                    DisplayName = "Codex Spark",
                    SupportsChildProviderIds = false,
                    ShowInSettings = false,
                    HandledProviderIds = ["codex.spark"],
                },
            ],
        };

        var items = ProviderSettingsDisplayCatalog.CreateDisplayItems(configs, Array.Empty<ProviderUsage>(), capabilities);

        Assert.Contains(items, item => string.Equals(item.Config.ProviderId, "codex", StringComparison.Ordinal));
        Assert.DoesNotContain(items, item => string.Equals(item.Config.ProviderId, "codex.spark", StringComparison.Ordinal));
    }
}
