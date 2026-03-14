// <copyright file="ProviderSettingsCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSettingsCatalog
{
    public static ProviderSettingsBehavior Resolve(
        ProviderConfig config,
        ProviderUsage? usage,
        bool isDerived)
    {
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(config.ProviderId);
        var hasSessionToken = IsSessionToken(config.ApiKey);
        var inputMode = isDerived
            ? ProviderInputMode.DerivedReadOnly
            : ResolveInputMode(canonicalProviderId, usage, hasSessionToken);
        var isInactive = isDerived
            ? false
            : inputMode switch
            {
                ProviderInputMode.AutoDetectedStatus => usage == null || !usage.IsAvailable,
                ProviderInputMode.SessionAuthStatus => string.IsNullOrWhiteSpace(config.ApiKey) && !(usage?.IsAvailable == true),
                _ => string.IsNullOrWhiteSpace(config.ApiKey),
            };
        var sessionProviderLabel = inputMode == ProviderInputMode.SessionAuthStatus
            ? ProviderMetadataCatalog.GetSessionStatusLabel(canonicalProviderId)
            : null;

        return new ProviderSettingsBehavior(
            InputMode: inputMode,
            IsInactive: isInactive,
            IsDerivedVisible: ProviderMetadataCatalog.IsVisibleDerivedProviderId(config.ProviderId ?? string.Empty),
            SessionProviderLabel: sessionProviderLabel);
    }

    public static bool IsSessionToken(string? apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) &&
               !apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderInputMode ResolveInputMode(string canonicalProviderId, ProviderUsage? usage, bool hasSessionToken)
    {
        var settingsMode = ProviderMetadataCatalog.GetSettingsMode(canonicalProviderId);
        if (settingsMode == ProviderSettingsMode.SessionAuthStatus &&
            ProviderMetadataCatalog.UsesSessionAuthStatusWhenQuotaBasedOrSessionToken(canonicalProviderId) &&
            usage?.IsQuotaBased != true &&
            !hasSessionToken)
        {
            settingsMode = ProviderSettingsMode.StandardApiKey;
        }

        return settingsMode switch
        {
            ProviderSettingsMode.AutoDetectedStatus => ProviderInputMode.AutoDetectedStatus,
            ProviderSettingsMode.ExternalAuthStatus => ProviderInputMode.ExternalAuthStatus,
            ProviderSettingsMode.SessionAuthStatus => ProviderInputMode.SessionAuthStatus,
            _ => ProviderInputMode.StandardApiKey,
        };
    }
}
