// <copyright file="ProviderSettingsDisplayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSettingsDisplayCatalog
{
    public static IReadOnlyList<ProviderSettingsDisplayItem> CreateDisplayItems(
        IReadOnlyCollection<ProviderConfig> configs,
        IReadOnlyCollection<ProviderUsage> usages,
        AgentProviderCapabilitiesSnapshot? capabilities = null)
    {
        var displayItems = configs
            .Where(config => ProviderCapabilityCatalog.ShouldShowInSettings(config.ProviderId, capabilities))
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false))
            .ToList();
        var configuredProviderIds = displayItems
            .Select(item => item.Config.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultProviderIds = ProviderCapabilityCatalog.GetDefaultSettingsProviderIds(capabilities)
            .Where(providerId => !configuredProviderIds.Contains(providerId))
            .ToList();

        var defaultItems = defaultProviderIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateDefaultDisplayConfig)
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false));
        var explicitDisplayProviderIds = configuredProviderIds
            .Concat(defaultProviderIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var derivedItems = usages
            .Where(usage =>
                ProviderCapabilityCatalog.IsVisibleDerivedProviderId(usage.ProviderId ?? string.Empty, capabilities) &&
                !explicitDisplayProviderIds.Contains(usage.ProviderId))
            .Select(usage => new ProviderSettingsDisplayItem(CreateDerivedConfig(usage), IsDerived: true));

        displayItems.AddRange(defaultItems);
        displayItems.AddRange(derivedItems);

        return displayItems
            .OrderBy(item => ProviderCapabilityCatalog.GetDisplayName(item.Config.ProviderId, capabilities), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Config.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ProviderConfig CreateDefaultDisplayConfig(string providerId)
    {
        if (ProviderMetadataCatalog.TryCreateDefaultConfig(providerId, out var config))
        {
            return config;
        }

        return new ProviderConfig
        {
            ProviderId = providerId,
        };
    }

    private static ProviderConfig CreateDerivedConfig(ProviderUsage usage)
    {
        return new ProviderConfig
        {
            ProviderId = usage.ProviderId,
            Type = usage.IsQuotaBased ? "quota-based" : "pay-as-you-go",
            PlanType = usage.PlanType,
        };
    }
}
