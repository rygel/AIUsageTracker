using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderSettingsDisplayItem(ProviderConfig Config, bool IsDerived);
`n
internal static class ProviderSettingsDisplayCatalog
{
    public static IReadOnlyList<ProviderSettingsDisplayItem> CreateDisplayItems(
        IReadOnlyCollection<ProviderConfig> configs,
        IReadOnlyCollection<ProviderUsage> usages)
    {
        var displayItems = configs
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false))
            .ToList();

        var configuredProviderIds = configs
            .Select(config => config.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var defaultItems = ProviderMetadataCatalog.Definitions
            .Select(definition => definition.ProviderId)
            .Where(providerId => !configuredProviderIds.Contains(providerId))
            .Select(CreateDefaultDisplayConfig)
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false));

        displayItems.AddRange(defaultItems);

        var derivedItems = usages
            .Where(usage =>
                ProviderSettingsCatalog.IsDerivedProviderVisible(usage.ProviderId) &&
                !configuredProviderIds.Contains(usage.ProviderId))
            .Select(usage => new ProviderSettingsDisplayItem(CreateDerivedConfig(usage), IsDerived: true));

        displayItems.AddRange(derivedItems);

        return displayItems
            .OrderBy(item => ProviderMetadataCatalog.GetDisplayName(item.Config.ProviderId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Config.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
`n
    private static ProviderConfig CreateDefaultDisplayConfig(string providerId)
    {
        if (ProviderMetadataCatalog.TryCreateDefaultConfig(providerId, out var config))
        {
            return config;
        }

        return new ProviderConfig
        {
            ProviderId = providerId
        };
    }
`n
    private static ProviderConfig CreateDerivedConfig(ProviderUsage usage)
    {
        return new ProviderConfig
        {
            ProviderId = usage.ProviderId,
            Type = usage.IsQuotaBased ? "quota-based" : "pay-as-you-go",
            PlanType = usage.PlanType
        };
    }
}
