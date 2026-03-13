// <copyright file="ProviderCapabilityCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
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

    public static string ResolveDisplayLabel(
        string providerId,
        string? runtimeLabel)
    {
        return ProviderMetadataCatalog.ResolveDisplayLabel(providerId, runtimeLabel);
    }

    public static string ResolveDisplayLabel(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return ResolveDisplayLabel(usage.ProviderId ?? string.Empty, usage.ProviderName);
    }

    public static string GetDisplayName(
        string providerId,
        string? providerName)
    {
        return ResolveDisplayLabel(providerId, providerName);
    }

    public static string GetDisplayName(string providerId)
    {
        return ProviderMetadataCatalog.GetConfiguredDisplayName(providerId);
    }

    public static string GetConfiguredDisplayName(string providerId)
    {
        return ProviderMetadataCatalog.GetConfiguredDisplayName(providerId);
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
        return ProviderMetadataCatalog.ShouldUseSharedSubDetailCollapsePreference(providerId);
    }

    public static bool ShouldRenderAsSettingsSubItem(string providerId)
    {
        return ProviderMetadataCatalog.ShouldRenderAsSettingsSubItem(providerId);
    }

    public static bool HasVisibleDerivedProviders(string providerId)
    {
        return ProviderMetadataCatalog.HasDisplayableDerivedProviders(providerId);
    }
}
