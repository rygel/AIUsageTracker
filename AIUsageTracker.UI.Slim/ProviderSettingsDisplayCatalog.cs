// <copyright file="ProviderSettingsDisplayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSettingsDisplayCatalog
{
    public static IReadOnlyList<ProviderSettingsDisplayItem> CreateDisplayItems(
        IReadOnlyCollection<ProviderConfig> configs,
        IReadOnlyCollection<ProviderUsage> usages)
    {
        var displayItems = configs
            .Where(config => ProviderMetadataCatalog.ShouldShowInSettings(config.ProviderId))
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false))
            .ToList();
        var configuredProviderIds = displayItems
            .Select(item => item.Config.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultProviderIds = ProviderMetadataCatalog.GetDefaultSettingsProviderIds()
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
            .Select(usage => new { Usage = usage, ProviderId = usage.ProviderId ?? string.Empty })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.ProviderId) &&
                ProviderMetadataCatalog.IsVisibleDerivedProviderId(x.ProviderId) &&
                !explicitDisplayProviderIds.Contains(x.ProviderId))
            .Select(x => x.Usage)
            .Select(usage => new ProviderSettingsDisplayItem(CreateDerivedConfig(usage), IsDerived: true));

        displayItems.AddRange(defaultItems);
        displayItems.AddRange(derivedItems);

        return displayItems
            .OrderBy(item => ProviderMetadataCatalog.ResolveDisplayLabel(item.Config.ProviderId), StringComparer.OrdinalIgnoreCase)
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
