// <copyright file="ProviderSubDetailSectionCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSubDetailSectionCatalog
{
    public static ProviderSubDetailSection? Build(ProviderUsage usage, AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(preferences);

        var details = ProviderSubDetailPresentationCatalog.GetDisplayableDetails(usage);
        if (details.Count == 0)
        {
            return null;
        }

        var providerId = usage.ProviderId ?? string.Empty;
        var title = $"{ProviderMetadataCatalog.ResolveDisplayLabel(usage)} Details";
        var isCollapsed = GetIsCollapsed(preferences, providerId);

        return new ProviderSubDetailSection(
            ProviderId: providerId,
            Title: title,
            Details: details,
            IsCollapsed: isCollapsed);
    }

    public static bool GetIsCollapsed(AppPreferences preferences, string providerId)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return ShouldUseSharedCollapsePreference(providerId) && preferences.IsAntigravityCollapsed;
    }

    public static void SetIsCollapsed(AppPreferences preferences, string providerId, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!ShouldUseSharedCollapsePreference(providerId))
        {
            return;
        }

        preferences.IsAntigravityCollapsed = isCollapsed;
    }

    private static bool ShouldUseSharedCollapsePreference(string providerId)
    {
        return ProviderMetadataCatalog.ShouldUseSharedSubDetailCollapsePreference(providerId ?? string.Empty);
    }
}
