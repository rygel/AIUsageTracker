// <copyright file="ProviderCapabilityCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderCapabilityCatalog
{
    public static bool ShouldShowInMainWindow(string providerId, AgentProviderCapabilitiesSnapshot? snapshot)
    {
        return TryGetCapability(providerId, snapshot, out _) || ProviderMetadataCatalog.ShouldShowInMainWindow(providerId);
    }

    public static bool ShouldShowInSettings(string providerId, AgentProviderCapabilitiesSnapshot? snapshot)
    {
        return TryGetCapability(providerId, snapshot, out var capability)
            ? capability.ShowInSettings
            : ProviderMetadataCatalog.ShouldShowInSettings(providerId);
    }

    public static bool SupportsAccountIdentity(string providerId, AgentProviderCapabilitiesSnapshot? snapshot)
    {
        return TryGetCapability(providerId, snapshot, out var capability)
            ? capability.SupportsAccountIdentity
            : ProviderMetadataCatalog.SupportsAccountIdentity(providerId);
    }

    public static bool IsVisibleDerivedProviderId(string providerId, AgentProviderCapabilitiesSnapshot? snapshot)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        if (snapshot?.Providers is { Count: > 0 })
        {
            return snapshot.Providers.Any(capability =>
                capability.VisibleDerivedProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase));
        }

        return ProviderMetadataCatalog.IsVisibleDerivedProviderId(providerId);
    }

    public static IReadOnlyList<string> GetDefaultSettingsProviderIds(AgentProviderCapabilitiesSnapshot? snapshot)
    {
        if (snapshot?.Providers is { Count: > 0 })
        {
            return snapshot.Providers
                .Where(capability => capability.ShowInSettings)
                .SelectMany(capability => new[] { capability.ProviderId }.Concat(capability.SettingsAdditionalProviderIds))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return ProviderMetadataCatalog.GetDefaultSettingsProviderIds();
    }

    public static string GetCanonicalProviderId(string providerId, AgentProviderCapabilitiesSnapshot? snapshot)
    {
        if (TryGetCapability(providerId, snapshot, out var capability))
        {
            return capability.ProviderId;
        }

        return ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
    }

    public static string GetDisplayName(
        string providerId,
        string? providerName,
        AgentProviderCapabilitiesSnapshot? snapshot)
    {
        if (TryGetCapability(providerId, snapshot, out var capability))
        {
            var isDerivedProviderId = !string.Equals(providerId, capability.ProviderId, StringComparison.OrdinalIgnoreCase);
            if (isDerivedProviderId && !string.IsNullOrWhiteSpace(providerName))
            {
                return providerName;
            }

            if (!string.IsNullOrWhiteSpace(capability.DisplayName))
            {
                return capability.DisplayName;
            }
        }

        return ProviderMetadataCatalog.GetDisplayName(providerId, providerName);
    }

    public static string GetDisplayName(string providerId, AgentProviderCapabilitiesSnapshot? snapshot)
    {
        return GetDisplayName(providerId, providerName: null, snapshot);
    }

    public static bool ShouldCollapseDerivedChildrenInMainWindow(
        string providerId,
        AgentProviderCapabilitiesSnapshot? snapshot)
    {
        return TryGetCapability(providerId, snapshot, out var capability)
            ? capability.CollapseDerivedChildrenInMainWindow
            : ProviderMetadataCatalog.ShouldCollapseDerivedChildrenInMainWindow(providerId);
    }

    public static bool ShouldRenderAggregateDetailsInMainWindow(
        string providerId,
        AgentProviderCapabilitiesSnapshot? snapshot)
    {
        return TryGetCapability(providerId, snapshot, out var capability)
            ? capability.RenderAggregateDetailsInMainWindow
            : ProviderMetadataCatalog.ShouldRenderAggregateDetailsInMainWindow(providerId);
    }

    public static bool ShouldUseSharedSubDetailCollapsePreference(
        string providerId,
        AgentProviderCapabilitiesSnapshot? snapshot)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId, snapshot);
        return ShouldCollapseDerivedChildrenInMainWindow(canonicalProviderId, snapshot);
    }

    public static bool ShouldRenderAsSettingsSubItem(string providerId, AgentProviderCapabilitiesSnapshot? snapshot)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId, snapshot);
        var isCanonicalChild = !string.Equals(canonicalProviderId, providerId, StringComparison.OrdinalIgnoreCase);
        return isCanonicalChild && ShouldUseSharedSubDetailCollapsePreference(canonicalProviderId, snapshot);
    }

    public static bool HasVisibleDerivedProviders(string providerId, AgentProviderCapabilitiesSnapshot? snapshot)
    {
        if (TryGetCapability(providerId, snapshot, out var capability))
        {
            return capability.VisibleDerivedProviderIds.Count > 0;
        }

        return ProviderMetadataCatalog.TryGet(providerId, out var definition) &&
               definition.VisibleDerivedProviderIds.Count > 0;
    }

    private static bool TryGetCapability(
        string providerId,
        AgentProviderCapabilitiesSnapshot? snapshot,
        out AgentProviderCapabilityDefinition capability)
    {
        if (string.IsNullOrWhiteSpace(providerId) || snapshot?.Providers is not { Count: > 0 })
        {
            capability = null!;
            return false;
        }

        capability = snapshot.Providers.FirstOrDefault(candidate => HandlesProviderId(candidate, providerId))!;
        return capability != null;
    }

    private static bool HandlesProviderId(AgentProviderCapabilityDefinition capability, string providerId)
    {
        if (capability.HandledProviderIds.Count == 0)
        {
            return false;
        }

        if (capability.HandledProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!capability.SupportsChildProviderIds)
        {
            return false;
        }

        return capability.HandledProviderIds.Any(handled =>
            providerId.StartsWith($"{handled}.", StringComparison.OrdinalIgnoreCase));
    }
}
