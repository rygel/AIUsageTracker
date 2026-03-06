using System.Reflection;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

public static class ProviderMetadataCatalog
{
    private static readonly Lazy<IReadOnlyList<ProviderDefinition>> DefinitionsValue = new(LoadDefinitions);

    public static IReadOnlyList<ProviderDefinition> Definitions => DefinitionsValue.Value;

    public static ProviderDefinition? Find(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return Definitions.FirstOrDefault(definition => definition.HandlesProviderId(providerId));
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

    public static string GetDisplayName(string providerId, string? providerName = null)
    {
        if (TryGet(providerId, out var definition))
        {
            var mapped = definition.ResolveDisplayName(providerId);
            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return providerName;
        }

        return providerId ?? string.Empty;
    }

    public static bool IsAutoIncluded(string providerId)
    {
        return TryGet(providerId, out var definition) && definition.AutoIncludeWhenUnconfigured;
    }

    public static string GetCanonicalProviderId(string providerId)
    {
        if (TryGet(providerId, out var definition))
        {
            return definition.ProviderId;
        }

        return providerId ?? string.Empty;
    }

    public static bool IsAggregateParentProviderId(string providerId)
    {
        return string.Equals(providerId, "antigravity", StringComparison.OrdinalIgnoreCase);
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

    public static bool ShouldPersistProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
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

    public static bool ShouldSuppressUsageProviderId(IReadOnlyCollection<ProviderConfig> configs, string providerId)
    {
        if (!TryGet(providerId, out var definition) ||
            string.IsNullOrWhiteSpace(definition.SessionAuthCanonicalProviderId) ||
            string.Equals(definition.SessionAuthCanonicalProviderId, definition.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasConfiguredCanonicalConfig(configs, definition.SessionAuthCanonicalProviderId) &&
               configs.Any(config =>
                   string.Equals(config.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                   IsSessionAuthConfig(config, definition));
    }

    public static bool ShouldSuppressConfig(IReadOnlyCollection<ProviderConfig> configs, ProviderConfig config)
    {
        return ShouldSuppressUsageProviderId(configs, config.ProviderId);
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

        config = new ProviderConfig
        {
            ProviderId = providerId,
            ApiKey = apiKey ?? string.Empty,
            Type = definition.DefaultConfigType,
            PlanType = definition.PlanType,
            AuthSource = authSource ?? "Unknown",
            Description = description
        };

        return true;
    }

    public static void NormalizeCanonicalConfigurations(List<ProviderConfig> configs)
    {
        NormalizeConfigOwnership(configs);
    }

    public static bool ShouldSuppressOpenAiSession(IReadOnlyCollection<ProviderConfig> configs)
    {
        return configs.Any(config =>
            IsSessionAuthConfig(config) &&
            HasConfiguredCanonicalConfig(configs, GetCanonicalConfigOwnerId(config)));
    }

    public static bool IsOpenAiSessionConfig(ProviderConfig config)
    {
        return IsSessionAuthConfig(config);
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

        return definitions;
    }

    private static void NormalizeConfigOwnership(List<ProviderConfig> configs)
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

        configs.RemoveAll(config =>
        {
            var ownerProviderId = GetCanonicalConfigOwnerId(config);
            return !string.Equals(ownerProviderId, config.ProviderId, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static ProviderConfig? GetOrCreateConfig(List<ProviderConfig> configs, string providerId)
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

    private static bool HasConfiguredCanonicalConfig(IEnumerable<ProviderConfig> configs, string providerId)
    {
        return configs.Any(config =>
            string.Equals(config.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(config.ApiKey));
    }

    private static string GetCanonicalConfigOwnerId(ProviderConfig config)
    {
        if (TryGet(config.ProviderId, out var definition) && IsSessionAuthConfig(config, definition))
        {
            return definition.SessionAuthCanonicalProviderId!;
        }

        return GetCanonicalProviderId(config.ProviderId);
    }

    private static bool IsSessionAuthConfig(ProviderConfig config)
    {
        return TryGet(config.ProviderId, out var definition) && IsSessionAuthConfig(config, definition);
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
             string.Equals(canonicalConfig.AuthSource, "Unknown", StringComparison.OrdinalIgnoreCase)) &&
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
                definition.ProviderId
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
}
