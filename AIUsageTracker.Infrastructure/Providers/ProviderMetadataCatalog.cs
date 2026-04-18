// <copyright file="ProviderMetadataCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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

    public static string GetProviderOwnerId(string providerId)
    {
        if (IsVisibleDerivedProviderId(providerId))
        {
            return providerId;
        }

        return Find(providerId)?.ProviderId ?? providerId ?? string.Empty;
    }

    public static string GetConfiguredDisplayName(string providerId)
    {
        var definition = Find(providerId);
        if (definition == null)
        {
            return providerId ?? string.Empty;
        }

        var mapped = definition.ResolveDisplayName(providerId);
        return !string.IsNullOrWhiteSpace(mapped) ? mapped : definition.DisplayName;
    }

    public static string GetDerivedModelDisplayName(string providerId, string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return modelName;
        }

        var definition = Find(providerId);
        if (definition != null && !string.IsNullOrWhiteSpace(definition.DerivedModelDisplaySuffix))
        {
            return $"{modelName} {definition.DerivedModelDisplaySuffix}";
        }

        return modelName;
    }

    public static string GetIconAssetName(string providerId)
    {
        var definition = Find(providerId);
        return definition != null && !string.IsNullOrWhiteSpace(definition.IconAssetName)
            ? definition.IconAssetName
            : providerId ?? string.Empty;
    }

    public static bool TryGetBadgeDefinition(string providerId, out string colorHex, out string initial)
    {
        colorHex = string.Empty;
        initial = string.Empty;

        var definition = Find(providerId);
        if (definition == null ||
            string.IsNullOrWhiteSpace(definition.BadgeColorHex) ||
            string.IsNullOrWhiteSpace(definition.BadgeInitial))
        {
            return false;
        }

        colorHex = definition.BadgeColorHex;
        initial = definition.BadgeInitial;
        return true;
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

    public static IReadOnlyList<string> GetProviderIdsWithDiscoveryEnvironmentVariables()
    {
        return Definitions
            .Where(definition => definition.DiscoveryEnvironmentVariables.Count > 0)
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
                ShouldPersistProviderId(definition.ProviderId))
            .Select(definition => definition.ProviderId)
            .OrderBy(providerId => providerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    public static IReadOnlySet<string> ExpandAcceptedUsageProviderIds(IEnumerable<string> providerIds)
    {
        ArgumentNullException.ThrowIfNull(providerIds);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerId in providerIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            expanded.Add(providerId);
            var definition = Find(providerId);
            if (definition == null)
            {
                continue;
            }

            expanded.UnionWith(definition.CoReportedProviderIds.Where(id => !string.IsNullOrWhiteSpace(id)));
        }

        return expanded;
    }

    public static bool ShouldPersistProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        var definition = Find(providerId);
        if (definition == null)
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
            definition.SettingsAdditionalProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase));
    }

    public static bool TryCreateDefaultConfig(
        string providerId,
        out ProviderConfig config,
        string? apiKey = null,
        string? authSource = null,
        string? description = null)
    {
        var definition = Find(providerId);
        if (definition == null)
        {
            config = null!;
            return false;
        }

        config = definition.CreateDefaultConfig(providerId, apiKey, authSource, description);
        return true;
    }

    private static List<ProviderDefinition> LoadDefinitions()
    {
        var definitions = new List<ProviderDefinition>
        {
            AntigravityProvider.StaticDefinition,
            ClaudeCodeProvider.StaticDefinition,
            CodexProvider.StaticDefinition,
            CodexProvider.SparkDefinition,
            DeepSeekProvider.StaticDefinition,
            GeminiProvider.StaticDefinition,
            GitHubCopilotProvider.StaticDefinition,
            KimiProvider.StaticDefinition,
            MinimaxProvider.StaticDefinition,
            MistralProvider.StaticDefinition,
            OpenAIProvider.StaticDefinition,
            OpenCodeZenProvider.StaticDefinition,
            OpenCodeProvider.StaticDefinition,
            OpenRouterProvider.StaticDefinition,
            SyntheticProvider.StaticDefinition,
            XiaomiProvider.StaticDefinition,
            ZaiProvider.StaticDefinition,
        };

        definitions.Sort((a, b) => string.Compare(a.ProviderId, b.ProviderId, StringComparison.OrdinalIgnoreCase));

        ValidateNoDuplicateProviderIds(definitions);
        ValidateNoDuplicateHandledProviderIds(definitions);
        ValidateDerivedModelSelectors(definitions);
        ValidateAggregateDetailContracts(definitions);

        return definitions;
    }

    private static void ValidateNoDuplicateProviderIds(IReadOnlyCollection<ProviderDefinition> definitions)
    {
        var duplicateProviderIds = definitions
            .GroupBy(definition => definition.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any())
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
            .Where(group => group.Select(item => item.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
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
                Missing = definition.VisibleDerivedProviderIds.Count == 0
                    ? Array.Empty<string>()
                    : definition.VisibleDerivedProviderIds
                        .Where(id => definition.DerivedModelSelectors.All(s =>
                            !string.Equals(s.DerivedProviderId, id, StringComparison.OrdinalIgnoreCase)))
                        .ToArray(),
            })
            .Where(entry => entry.Missing.Length > 0)
            .ToList();
        if (missingSelectors.Count > 0)
        {
            throw new InvalidOperationException(
                "Missing derived model selectors: " +
                string.Join("; ", missingSelectors.Select(e => $"{e.ProviderId}: {string.Join(", ", e.Missing)}")));
        }

        var unknownTargets = definitions
            .Select(definition => new
            {
                definition.ProviderId,
                Unknown = definition.DerivedModelSelectors
                    .Select(s => s.DerivedProviderId)
                    .Where(id => !definition.VisibleDerivedProviderIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            })
            .Where(entry => entry.Unknown.Length > 0)
            .ToList();
        if (unknownTargets.Count > 0)
        {
            throw new InvalidOperationException(
                "Derived model selectors reference unknown provider ids: " +
                string.Join("; ", unknownTargets.Select(e => $"{e.ProviderId}: {string.Join(", ", e.Unknown)}")));
        }
    }

    private static void ValidateAggregateDetailContracts(IReadOnlyCollection<ProviderDefinition> definitions)
    {
        var invalidVisibleDerivedProviderModes = definitions
            .Where(definition =>
                definition.VisibleDerivedProviderIds.Count > 0 &&
                definition.FamilyMode != ProviderFamilyMode.VisibleDerivedProviders)
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (invalidVisibleDerivedProviderModes.Count > 0)
        {
            throw new InvalidOperationException(
                "Providers with visible derived provider ids must use VisibleDerivedProviders family mode: " +
                string.Join(", ", invalidVisibleDerivedProviderModes));
        }

        var missingVisibleDerivedProviderIds = definitions
            .Where(definition =>
                definition.FamilyMode == ProviderFamilyMode.VisibleDerivedProviders &&
                definition.VisibleDerivedProviderIds.Count == 0)
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missingVisibleDerivedProviderIds.Count > 0)
        {
            throw new InvalidOperationException(
                "VisibleDerivedProviders family mode requires visible derived provider ids: " +
                string.Join(", ", missingVisibleDerivedProviderIds));
        }

        var invalidGroupedModelChildRowDefinitions = definitions
            .Where(definition =>
                definition.UseChildProviderRowsForGroupedModels &&
                !definition.SupportsChildProviderIds)
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (invalidGroupedModelChildRowDefinitions.Count > 0)
        {
            throw new InvalidOperationException(
                "Providers using child provider rows for grouped models must support child provider ids: " +
                string.Join(", ", invalidGroupedModelChildRowDefinitions));
        }
    }
}
