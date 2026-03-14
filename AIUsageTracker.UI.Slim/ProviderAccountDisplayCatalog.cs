// <copyright file="ProviderAccountDisplayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderAccountDisplayCatalog
{
    public static string ResolveDisplayAccountName(
        string providerId,
        string? usageAccountName,
        bool isPrivacyMode)
    {
        if (!ProviderMetadataCatalog.SupportsAccountIdentity(providerId))
        {
            return string.Empty;
        }

        var accountName = NormalizeIdentity(usageAccountName);

        if (string.IsNullOrWhiteSpace(accountName))
        {
            return string.Empty;
        }

        return isPrivacyMode
            ? ProviderStatusPresentationCatalog.MaskAccountIdentifier(accountName)
            : accountName;
    }

    private static string NormalizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized is "Unknown" or "User"
            ? string.Empty
            : normalized;
    }
}
