// <copyright file="ProviderCapabilityCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderCapabilityCatalog
{
    public static bool ShouldShowInMainWindow(string providerId)
    {
        return ProviderMetadataCatalog.ShouldShowInMainWindow(providerId);
    }

    public static bool ShouldShowInSettings(string providerId)
    {
        return ProviderMetadataCatalog.ShouldShowInSettings(providerId);
    }

    public static bool SupportsAccountIdentity(string providerId)
    {
        return ProviderMetadataCatalog.SupportsAccountIdentity(providerId);
    }

    public static bool IsVisibleDerivedProviderId(string providerId)
    {
        return ProviderMetadataCatalog.IsVisibleDerivedProviderId(providerId);
    }

    public static IReadOnlyList<string> GetDefaultSettingsProviderIds()
    {
        return ProviderMetadataCatalog.GetDefaultSettingsProviderIds();
    }

    public static string GetCanonicalProviderId(string providerId)
    {
        return ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
    }

    public static string GetDisplayName(
        string providerId,
        string? providerName)
    {
        return ProviderMetadataCatalog.GetDisplayName(providerId, providerName);
    }

    public static string GetDisplayName(string providerId)
    {
        return GetDisplayName(providerId, providerName: null);
    }

    public static bool ShouldCollapseDerivedChildrenInMainWindow(string providerId)
    {
        return ProviderMetadataCatalog.ShouldCollapseDerivedChildrenInMainWindow(providerId);
    }

    public static bool ShouldRenderAggregateDetailsInMainWindow(string providerId)
    {
        return ProviderMetadataCatalog.ShouldRenderAggregateDetailsInMainWindow(providerId);
    }

    public static bool ShouldUseSharedSubDetailCollapsePreference(string providerId)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        return ShouldCollapseDerivedChildrenInMainWindow(canonicalProviderId);
    }

    public static bool ShouldRenderAsSettingsSubItem(string providerId)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        var isCanonicalChild = !string.Equals(canonicalProviderId, providerId, StringComparison.OrdinalIgnoreCase);
        return isCanonicalChild && ShouldUseSharedSubDetailCollapsePreference(canonicalProviderId);
    }

    public static bool HasVisibleDerivedProviders(string providerId)
    {
        return ProviderMetadataCatalog.TryGet(providerId, out var definition) &&
               definition.VisibleDerivedProviderIds.Count > 0;
    }
}
