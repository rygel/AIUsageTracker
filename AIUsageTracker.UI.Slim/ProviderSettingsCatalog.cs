// <copyright file="ProviderSettingsCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Infrastructure.Providers;

    internal enum ProviderInputMode
    {
        StandardApiKey,
        DerivedReadOnly,
        AntigravityAutoDetected,
        GitHubCopilotAuthStatus,
        OpenAiSessionStatus
    }
    `n
    internal sealed record ProviderSettingsBehavior(
        ProviderInputMode InputMode,
        bool IsInactive,
        bool IsDerivedVisible,
        string? SessionProviderLabel,
        bool PreferCodexIdentity);
    `n
    internal static class ProviderSettingsCatalog
    {
        public static ProviderSettingsBehavior Resolve(ProviderConfig config, ProviderUsage? usage, bool isDerived)
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
                    ProviderInputMode.AntigravityAutoDetected => usage == null || !usage.IsAvailable,
                    ProviderInputMode.OpenAiSessionStatus => string.IsNullOrWhiteSpace(config.ApiKey) && !(usage?.IsAvailable == true),
                    _ => string.IsNullOrWhiteSpace(config.ApiKey)
                };

            var sessionProviderLabel = inputMode == ProviderInputMode.OpenAiSessionStatus
                ? ProviderMetadataCatalog.GetSessionStatusLabel(canonicalProviderId)
                : null;

            return new ProviderSettingsBehavior(
                InputMode: inputMode,
                IsInactive: isInactive,
                IsDerivedVisible: IsDerivedProviderVisible(config.ProviderId),
                SessionProviderLabel: sessionProviderLabel,
                PreferCodexIdentity: inputMode == ProviderInputMode.OpenAiSessionStatus &&
                                     ProviderMetadataCatalog.GetSessionIdentitySource(canonicalProviderId) == ProviderSessionIdentitySource.Codex);
        }
    `n
        public static bool IsDerivedProviderVisible(string? providerId)
        {
            return ProviderMetadataCatalog.IsVisibleDerivedProviderId(providerId ?? string.Empty);
        }
    `n
        public static bool IsSessionToken(string? apiKey)
        {
            return !string.IsNullOrWhiteSpace(apiKey) &&
                   !apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
        }
    `n
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
                ProviderSettingsMode.AutoDetectedStatus => ProviderInputMode.AntigravityAutoDetected,
                ProviderSettingsMode.ExternalAuthStatus => ProviderInputMode.GitHubCopilotAuthStatus,
                ProviderSettingsMode.SessionAuthStatus => ProviderInputMode.OpenAiSessionStatus,
                _ => ProviderInputMode.StandardApiKey
            };
        }
    }
}
