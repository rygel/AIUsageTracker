// <copyright file="ProviderMetadataCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

public static class ProviderMetadataCatalog
{
    private const string LegacyOpenAiProviderId = "openai";
    private static readonly Lazy<IReadOnlyList<ProviderDefinition>> DefinitionsValue = new(LoadDefinitions);

    public static IReadOnlyList<ProviderDefinition> Definitions => DefinitionsValue.Value;

    public static ProviderDefinition? Find(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return Definitions.FirstOrDefault(definition =>
            ProviderFamilyPolicy.BelongsToProviderFamily(definition.HandledProviderIds, providerId, definition.FamilyMode));
    }

    public static bool TryGet(string providerId, out ProviderDefinition definition)
    {
        var found = Find(providerId);
        if (found == null)
        {
            definition = null!;
            return false;
        }

        definition = found;
        return true;
    }

    public static string GetConfiguredDisplayName(string providerId)
    {
        if (TryGet(providerId, out var definition))
        {
            var mapped = definition.ResolveDisplayName(providerId);
            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            return definition.DisplayName;
        }

        return providerId ?? string.Empty;
    }

    public static string ResolveDisplayLabel(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return ResolveDisplayLabel(usage.ProviderId ?? string.Empty, usage.ProviderName);
    }

    public static string ResolveDisplayLabel(string providerId, string? runtimeLabel = null)
    {
        if (TryGet(providerId, out var definition))
        {
            var isDerivedProviderId = !string.Equals(
                providerId,
                definition.ProviderId,
                StringComparison.OrdinalIgnoreCase);
            var mapped = definition.ResolveDisplayName(providerId);

            if (!string.IsNullOrWhiteSpace(mapped) &&
                (!isDerivedProviderId ||
                 definition.PreferDisplayNameOverridesForDerivedProviderIds ||
                 string.IsNullOrWhiteSpace(runtimeLabel)))
            {
                return mapped;
            }

            if (isDerivedProviderId && !string.IsNullOrWhiteSpace(runtimeLabel))
            {
                return runtimeLabel;
            }

            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            return definition.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(runtimeLabel))
        {
            return runtimeLabel;
        }

        return providerId ?? string.Empty;
    }

    public static string GetDerivedModelDisplayName(string providerId, string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return modelName;
        }

        if (TryGet(providerId, out var definition) &&
            !string.IsNullOrWhiteSpace(definition.DerivedModelDisplaySuffix))
        {
            return $"{modelName} {definition.DerivedModelDisplaySuffix}";
        }

        return modelName;
    }

    public static bool IsAutoIncluded(string providerId)
    {
        return TryGet(providerId, out var definition) && definition.AutoIncludeWhenUnconfigured;
    }

    public static bool TryGetUsageSemantics(string providerId, out PlanType planType, out bool isQuotaBased)
    {
        if (TryGet(providerId, out var definition))
        {
            planType = definition.PlanType;
            isQuotaBased = definition.IsQuotaBased;
            return true;
        }

        planType = default;
        isQuotaBased = false;
        return false;
    }

    public static string GetCanonicalProviderId(string providerId)
    {
        if (TryGet(providerId, out var definition))
        {
            return definition.ProviderId;
        }

        return providerId ?? string.Empty;
    }

    public static bool BelongsToProviderFamily(string providerId, string candidateProviderId)
    {
        return TryGet(providerId, out var definition) &&
               ProviderFamilyPolicy.BelongsToProviderFamily(definition.HandledProviderIds, candidateProviderId, definition.FamilyMode);
    }

    public static bool IsChildProviderId(string providerId)
    {
        if (!TryGet(providerId, out var definition))
        {
            return false;
        }

        return ProviderFamilyPolicy.IsChildProviderId(definition.HandledProviderIds, providerId, definition.FamilyMode);
    }

    public static bool IsChildProviderId(string parentProviderId, string candidateProviderId)
    {
        return TryGet(parentProviderId, out var definition) &&
               ProviderFamilyPolicy.IsChildProviderId(definition.HandledProviderIds, candidateProviderId, definition.FamilyMode);
    }

    public static bool TryGetChildProviderKey(string parentProviderId, string candidateProviderId, out string childProviderKey)
    {
        childProviderKey = string.Empty;
        return TryGet(parentProviderId, out var definition) &&
               ProviderFamilyPolicy.TryGetChildProviderKey(
                   definition.HandledProviderIds,
                   candidateProviderId,
                   definition.FamilyMode,
                   out childProviderKey);
    }

    public static bool HasDisplayableDerivedProviders(string providerId)
    {
        return TryGet(providerId, out var definition) &&
               ProviderFamilyPolicy.HasDisplayableDerivedProviders(definition.VisibleDerivedProviderIds, definition.FamilyMode);
    }

    public static IReadOnlyCollection<string> GetVisibleDerivedProviderIds(string providerId)
    {
        return TryGet(providerId, out var definition)
            ? definition.VisibleDerivedProviderIds
            : Array.Empty<string>();
    }

    public static IReadOnlyCollection<ProviderDerivedModelSelector> GetDerivedModelSelectors(string providerId)
    {
        return TryGet(providerId, out var definition)
            ? definition.DerivedModelSelectors
            : Array.Empty<ProviderDerivedModelSelector>();
    }

    public static IReadOnlyList<string> GetMissingDerivedModelSelectorProviderIds(string providerId)
    {
        if (!TryGet(providerId, out var definition))
        {
            return Array.Empty<string>();
        }

        return GetMissingDerivedModelSelectorProviderIds(definition);
    }

    public static IReadOnlyList<string> GetUnknownDerivedModelSelectorProviderIds(string providerId)
    {
        if (!TryGet(providerId, out var definition))
        {
            return Array.Empty<string>();
        }

        return GetUnknownDerivedModelSelectorProviderIds(definition);
    }

    public static bool HasStaticVisibleDerivedProviders(string providerId)
    {
        return GetVisibleDerivedProviderIds(providerId).Count > 0;
    }

    public static string GetIconAssetName(string providerId)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        return TryGet(canonicalProviderId, out var definition) &&
               !string.IsNullOrWhiteSpace(definition.IconAssetName)
            ? definition.IconAssetName
            : canonicalProviderId;
    }

    public static bool TryGetFallbackBadgeDefinition(string providerId, out string colorHex, out string initial)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        colorHex = string.Empty;
        initial = string.Empty;

        if (!TryGet(canonicalProviderId, out var definition) ||
            string.IsNullOrWhiteSpace(definition.FallbackBadgeColorHex) ||
            string.IsNullOrWhiteSpace(definition.FallbackBadgeInitial))
        {
            return false;
        }

        colorHex = definition.FallbackBadgeColorHex;
        initial = definition.FallbackBadgeInitial;
        return true;
    }

    public static bool IsAggregateParentProviderId(string providerId)
    {
        return TryGet(providerId, out var definition) &&
               string.Equals(providerId, definition.ProviderId, StringComparison.OrdinalIgnoreCase) &&
               ProviderFamilyPolicy.ShouldRenderSyntheticChildrenInMainWindow(definition.FamilyMode);
    }

    public static bool ShouldCollapseDerivedChildrenInMainWindow(string providerId)
    {
        return TryGet(providerId, out var definition) &&
               ProviderFamilyPolicy.ShouldCollapseDerivedChildrenInMainWindow(definition.FamilyMode);
    }

    public static bool ShouldUseChildProviderRowsForGroupedModels(string providerId)
    {
        return TryGet(providerId, out var definition) &&
               ProviderFamilyPolicy.UsesChildProviderRowsForGroupedModels(definition.FamilyMode);
    }

    public static bool IsTooltipOnlyProvider(string providerId)
    {
        return TryGet(providerId, out var definition) && definition.IsTooltipOnly;
    }

    public static bool ShouldShowInMainWindow(string providerId)
    {
        return TryGet(providerId, out var definition) && definition.ShowInMainWindow;
    }

    public static bool ShouldRenderAggregateDetailsInMainWindow(string providerId)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        return IsAggregateParentProviderId(canonicalProviderId);
    }

    public static string GetAggregateDetailDisplaySuffix(string providerId)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        if (TryGet(canonicalProviderId, out var definition) &&
            !string.IsNullOrWhiteSpace(definition.AggregateDetailDisplaySuffix))
        {
            return definition.AggregateDetailDisplaySuffix!;
        }

        var displayName = GetConfiguredDisplayName(canonicalProviderId);
        return $"[{displayName}]";
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

    public static ProviderDefinition? FindByEnvironmentVariable(string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName))
        {
            return null;
        }

        return Definitions.FirstOrDefault(definition =>
            definition.DiscoveryEnvironmentVariables.Contains(environmentVariableName, StringComparer.OrdinalIgnoreCase));
    }

    public static ProviderDefinition? FindByRooConfigProperty(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        return Definitions.FirstOrDefault(definition =>
            definition.RooConfigPropertyNames.Contains(propertyName, StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyCollection<string> GetDiscoveryEnvironmentVariables(string providerId)
    {
        return TryGet(providerId, out var definition)
            ? definition.DiscoveryEnvironmentVariables
            : Array.Empty<string>();
    }

    public static IReadOnlyList<string> GetProviderIdsWithDiscoveryEnvironmentVariables()
    {
        return Definitions
            .Where(definition => definition.DiscoveryEnvironmentVariables.Count > 0)
            .Select(definition => definition.ProviderId)
            .OrderBy(providerId => providerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> GetWellKnownProviderIds()
    {
        return Definitions
            .Where(definition => definition.IncludeInWellKnownProviders)
            .Select(definition => definition.ProviderId)
            .OrderBy(providerId => providerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> GetProviderIdsWithDedicatedSessionAuthFiles()
    {
        return Definitions
            .Where(definition =>
                definition.AuthIdentityCandidatePathTemplates.Count > 0 &&
                definition.SessionAuthFileSchemas.Count > 0 &&
                string.IsNullOrWhiteSpace(definition.SessionAuthCanonicalProviderId))
            .Select(definition => definition.ProviderId)
            .OrderBy(providerId => providerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool ShouldPersistProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        if (string.Equals(providerId, LegacyOpenAiProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGet(providerId, out var definition))
        {
            return true;
        }

        return !definition.NonPersistedProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsVisibleDerivedProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        return Definitions.Any(definition =>
            definition.VisibleDerivedProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase));
    }

    public static bool ShouldShowInSettings(string providerId)
    {
        return TryGet(providerId, out var definition) && definition.ShowInSettings;
    }

    public static bool SupportsAccountIdentity(string providerId)
    {
        return TryGet(providerId, out var definition) && definition.SupportsAccountIdentity;
    }

    public static IReadOnlyList<string> GetDefaultSettingsProviderIds()
    {
        return Definitions
            .Where(definition => definition.ShowInSettings)
            .SelectMany(definition => new[] { definition.ProviderId }.Concat(definition.SettingsAdditionalProviderIds))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> GetStartupRefreshProviderIds()
    {
        return Definitions
            .Where(definition => definition.RefreshOnStartupWithCachedData)
            .Select(definition => definition.ProviderId)
            .ToList();
    }

    public static ProviderSettingsMode GetSettingsMode(string providerId)
    {
        return TryGet(providerId, out var definition)
            ? definition.SettingsMode
            : ProviderSettingsMode.StandardApiKey;
    }

    public static bool UsesSessionAuthStatusWhenQuotaBasedOrSessionToken(string providerId)
    {
        return TryGet(providerId, out var definition) &&
               definition.UseSessionAuthStatusWhenQuotaBasedOrSessionToken;
    }

    public static string? GetSessionStatusLabel(string providerId)
    {
        return TryGet(providerId, out var definition)
            ? definition.SessionStatusLabel
            : null;
    }

    public static ProviderSessionIdentitySource GetSessionIdentitySource(string providerId)
    {
        return TryGet(providerId, out var definition)
            ? definition.SessionIdentitySource
            : ProviderSessionIdentitySource.None;
    }

    public static bool TryCreateDefaultConfig(
        string providerId,
        out ProviderConfig config,
        string? apiKey = null,
        string? authSource = null,
        string? description = null)
    {
        if (!TryGet(providerId, out var definition))
        {
            config = null!;
            return false;
        }

        config = definition.CreateDefaultConfig(providerId, apiKey, authSource, description);

        return true;
    }

    public static void NormalizeCanonicalConfigurations(IList<ProviderConfig> configs)
    {
        NormalizeConfigOwnership(configs);
    }

    private static IReadOnlyList<ProviderDefinition> LoadDefinitions()
    {
        var definitions = typeof(ProviderMetadataCatalog).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                typeof(IProviderService).IsAssignableFrom(type))
            .Select(ReadDefinition)
            .OrderBy(definition => definition.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (definitions.Count == 0)
        {
            throw new InvalidOperationException("No provider definitions were discovered.");
        }

        ValidateNoDuplicateProviderIds(definitions);
        ValidateNoDuplicateHandledProviderIds(definitions);
        ValidateDerivedModelSelectors(definitions);
        ValidateAggregateDetailContracts(definitions);

        return definitions;
    }

    private static void NormalizeConfigOwnership(IList<ProviderConfig> configs)
    {
        var nonCanonicalConfigs = configs
            .Where(config =>
            {
                var ownerProviderId = GetCanonicalConfigOwnerId(config);
                return !string.Equals(ownerProviderId, config.ProviderId, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (nonCanonicalConfigs.Count == 0)
        {
            return;
        }

        foreach (var sourceConfig in nonCanonicalConfigs)
        {
            var ownerProviderId = GetCanonicalConfigOwnerId(sourceConfig);
            var canonicalConfig = GetOrCreateConfig(configs, ownerProviderId);
            if (canonicalConfig == null)
            {
                continue;
            }

            MergeConfigIntoCanonical(sourceConfig, canonicalConfig);
        }

        foreach (var config in nonCanonicalConfigs)
        {
            configs.Remove(config);
        }
    }

    private static ProviderConfig? GetOrCreateConfig(IList<ProviderConfig> configs, string providerId)
    {
        var existingConfig = configs.FirstOrDefault(config =>
            string.Equals(config.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        if (existingConfig != null)
        {
            return existingConfig;
        }

        if (!TryCreateDefaultConfig(providerId, out var defaultConfig))
        {
            return null;
        }

        configs.Add(defaultConfig);
        return defaultConfig;
    }

    private static string GetCanonicalConfigOwnerId(ProviderConfig config)
    {
        if (ShouldRetainDedicatedConfig(config.ProviderId))
        {
            return config.ProviderId;
        }

        if (TryGet(config.ProviderId, out var definition) && IsSessionAuthConfig(config, definition))
        {
            return definition.SessionAuthCanonicalProviderId!;
        }

        return GetCanonicalProviderId(config.ProviderId);
    }

    private static bool ShouldRetainDedicatedConfig(string providerId)
    {
        return ShouldPersistProviderId(providerId) && IsVisibleDerivedProviderId(providerId);
    }

    private static bool IsSessionAuthConfig(ProviderConfig config, ProviderDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.SessionAuthCanonicalProviderId) ||
            string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return false;
        }

        if (definition.ExplicitApiKeyPrefixes.Count == 0)
        {
            return true;
        }

        return !definition.ExplicitApiKeyPrefixes.Any(prefix =>
            config.ApiKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static void MergeConfigIntoCanonical(ProviderConfig sourceConfig, ProviderConfig canonicalConfig)
    {
        if (string.IsNullOrWhiteSpace(canonicalConfig.ApiKey) && !string.IsNullOrWhiteSpace(sourceConfig.ApiKey))
        {
            canonicalConfig.ApiKey = sourceConfig.ApiKey;
        }

        if ((string.IsNullOrWhiteSpace(canonicalConfig.AuthSource) ||
             string.Equals(canonicalConfig.AuthSource, AuthSource.Unknown, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(sourceConfig.AuthSource))
        {
            canonicalConfig.AuthSource = sourceConfig.AuthSource;
        }

        var sourceDescription = GetPreferredMigrationDescription(sourceConfig);
        if (string.IsNullOrWhiteSpace(canonicalConfig.Description) && !string.IsNullOrWhiteSpace(sourceDescription))
        {
            canonicalConfig.Description = sourceDescription;
        }

        if (string.IsNullOrWhiteSpace(canonicalConfig.BaseUrl) && !string.IsNullOrWhiteSpace(sourceConfig.BaseUrl))
        {
            canonicalConfig.BaseUrl = sourceConfig.BaseUrl;
        }

        canonicalConfig.ShowInTray |= sourceConfig.ShowInTray;
        canonicalConfig.EnableNotifications |= sourceConfig.EnableNotifications;
    }

    private static string? GetPreferredMigrationDescription(ProviderConfig sourceConfig)
    {
        if (TryGet(sourceConfig.ProviderId, out var definition) &&
            IsSessionAuthConfig(sourceConfig, definition) &&
            !string.IsNullOrWhiteSpace(definition.SessionAuthMigrationDescription))
        {
            return definition.SessionAuthMigrationDescription;
        }

        return sourceConfig.Description;
    }

    private static ProviderDefinition ReadDefinition(Type providerType)
    {
        var staticDefinitionProperty = providerType.GetProperty(
            "StaticDefinition",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (staticDefinitionProperty?.PropertyType != typeof(ProviderDefinition))
        {
            throw new InvalidOperationException(
                $"Provider type '{providerType.FullName}' must expose a public static ProviderDefinition StaticDefinition property.");
        }

        if (staticDefinitionProperty.GetValue(null) is not ProviderDefinition definition)
        {
            throw new InvalidOperationException(
                $"Provider type '{providerType.FullName}' returned a null provider definition.");
        }

        return definition;
    }

    private static void ValidateNoDuplicateProviderIds(IReadOnlyCollection<ProviderDefinition> definitions)
    {
        var duplicateProviderIds = definitions
            .GroupBy(definition => definition.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (duplicateProviderIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate provider definitions detected: {string.Join(", ", duplicateProviderIds)}");
        }
    }

    private static void ValidateNoDuplicateHandledProviderIds(IReadOnlyCollection<ProviderDefinition> definitions)
    {
        var duplicateHandledIds = definitions
            .SelectMany(definition => definition.HandledProviderIds.Select(handledId => new
            {
                HandledId = handledId,
                definition.ProviderId,
            }))
            .GroupBy(item => item.HandledId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (duplicateHandledIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate handled provider ids detected: {string.Join(", ", duplicateHandledIds)}");
        }
    }

    private static void ValidateDerivedModelSelectors(IReadOnlyCollection<ProviderDefinition> definitions)
    {
        var missingSelectors = definitions
            .Select(definition => new
            {
                definition.ProviderId,
                Missing = GetMissingDerivedModelSelectorProviderIds(definition),
            })
            .Where(entry => entry.Missing.Count > 0)
            .ToList();
        if (missingSelectors.Count > 0)
        {
            throw new InvalidOperationException(
                "Missing derived model selectors: " +
                string.Join(
                    "; ",
                    missingSelectors.Select(entry => $"{entry.ProviderId}: {string.Join(", ", entry.Missing)}")));
        }

        var unknownSelectorTargets = definitions
            .Select(definition => new
            {
                definition.ProviderId,
                Unknown = GetUnknownDerivedModelSelectorProviderIds(definition),
            })
            .Where(entry => entry.Unknown.Count > 0)
            .ToList();
        if (unknownSelectorTargets.Count > 0)
        {
            throw new InvalidOperationException(
                "Derived model selectors reference unknown provider ids: " +
                string.Join(
                    "; ",
                    unknownSelectorTargets.Select(entry => $"{entry.ProviderId}: {string.Join(", ", entry.Unknown)}")));
        }
    }

    private static void ValidateAggregateDetailContracts(IReadOnlyCollection<ProviderDefinition> definitions)
    {
        var invalidVisibleDerivedProviderModes = definitions
            .Where(definition =>
                definition.VisibleDerivedProviderIds.Count > 0 &&
                definition.FamilyMode != ProviderFamilyMode.VisibleDerivedProviders &&
                definition.FamilyMode != ProviderFamilyMode.CollapsedDerivedProviders)
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (invalidVisibleDerivedProviderModes.Count > 0)
        {
            throw new InvalidOperationException(
                "Providers with visible derived provider ids must use a visible-derived family mode: " +
                string.Join(", ", invalidVisibleDerivedProviderModes));
        }

        var missingVisibleDerivedProviderIds = definitions
            .Where(definition =>
                (definition.FamilyMode == ProviderFamilyMode.VisibleDerivedProviders ||
                 definition.FamilyMode == ProviderFamilyMode.CollapsedDerivedProviders) &&
                definition.VisibleDerivedProviderIds.Count == 0)
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missingVisibleDerivedProviderIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Visible-derived family modes require visible derived provider ids: " +
                string.Join(", ", missingVisibleDerivedProviderIds));
        }

        var invalidAggregateDefinitions = definitions
            .Where(definition =>
                ProviderFamilyPolicy.ShouldRenderSyntheticChildrenInMainWindow(definition.FamilyMode) &&
                !ProviderFamilyPolicy.SupportsChildProviderIds(definition.FamilyMode))
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invalidAggregateDefinitions.Count > 0)
        {
            throw new InvalidOperationException(
                "Providers rendering synthetic aggregate children must support child provider ids: " +
                string.Join(", ", invalidAggregateDefinitions));
        }

        var invalidGroupedModelChildRowDefinitions = definitions
            .Where(definition =>
                ProviderFamilyPolicy.UsesChildProviderRowsForGroupedModels(definition.FamilyMode) &&
                (!ProviderFamilyPolicy.SupportsChildProviderIds(definition.FamilyMode) ||
                 ProviderFamilyPolicy.ShouldRenderSyntheticChildrenInMainWindow(definition.FamilyMode)))
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (invalidGroupedModelChildRowDefinitions.Count > 0)
        {
            throw new InvalidOperationException(
                "Providers using child provider rows for grouped models must support child provider ids and avoid synthetic child rendering: " +
                string.Join(", ", invalidGroupedModelChildRowDefinitions));
        }
    }

    private static IReadOnlyList<string> GetMissingDerivedModelSelectorProviderIds(ProviderDefinition definition)
    {
        if (definition.VisibleDerivedProviderIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        return definition.VisibleDerivedProviderIds
            .Where(derivedProviderId => definition.DerivedModelSelectors.All(selector =>
                !string.Equals(selector.DerivedProviderId, derivedProviderId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(derivedProviderId => derivedProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetUnknownDerivedModelSelectorProviderIds(ProviderDefinition definition)
    {
        return definition.DerivedModelSelectors
            .Select(selector => selector.DerivedProviderId)
            .Where(derivedProviderId => !definition.VisibleDerivedProviderIds.Contains(
                derivedProviderId,
                StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(derivedProviderId => derivedProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
