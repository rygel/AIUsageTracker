// <copyright file="ProviderMainWindowOrderingCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderMainWindowOrderingCatalog
{
    public static IEnumerable<ProviderUsage> OrderForMainWindow(IEnumerable<ProviderUsage> usages)
    {
        return usages
            .OrderByDescending(usage => usage.IsQuotaBased)
            .ThenBy(
                usage => GetFamilyDisplayName(usage),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                usage => ProviderCapabilityCatalog.GetDisplayName(
                    usage.ProviderId ?? string.Empty,
                    usage.ProviderName),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.ProviderId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetFamilyDisplayName(ProviderUsage usage)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        var canonicalProviderId = ProviderCapabilityCatalog.GetCanonicalProviderId(providerId);
        return ProviderCapabilityCatalog.GetDisplayName(canonicalProviderId, providerName: null);
    }
}
