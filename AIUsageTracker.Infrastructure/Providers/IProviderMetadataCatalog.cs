// <copyright file="IProviderMetadataCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

/// <summary>
/// Provides a catalog of known provider definitions for use by DI-injected components.
/// The static <see cref="ProviderMetadataCatalog"/> class implements the same contract via
/// the <see cref="ProviderMetadataCatalog.Default"/> singleton and static wrapper methods, so
/// all existing call sites continue to compile without changes.
/// </summary>
public interface IProviderMetadataCatalog
{
    IReadOnlyList<ProviderDefinition> Definitions { get; }

    ProviderDefinition? Find(string providerId);

    bool TryGet(string providerId, out ProviderDefinition definition);

    string GetCanonicalProviderId(string providerId);

    string GetConfiguredDisplayName(string providerId);

    string ResolveDisplayLabel(ProviderUsage usage);

    string ResolveDisplayLabel(string providerId, string? runtimeLabel = null);

    string GetDerivedModelDisplayName(string providerId, string modelName);

    string GetIconAssetName(string providerId);

    bool TryGetBadgeDefinition(string providerId, out string colorHex, out string initial);

    ProviderDefinition? FindByEnvironmentVariable(string environmentVariableName);

    ProviderDefinition? FindByRooConfigProperty(string propertyName);

    IReadOnlyList<string> GetProviderIdsWithDiscoveryEnvironmentVariables();

    IReadOnlyList<string> GetProviderIdsWithDedicatedSessionAuthFiles();

    IReadOnlyList<string> GetDefaultSettingsProviderIds();

    IReadOnlyList<string> GetStartupRefreshProviderIds();

    bool ShouldPersistProviderId(string providerId);

    bool IsVisibleDerivedProviderId(string providerId);

    bool TryCreateDefaultConfig(
        string providerId,
        out ProviderConfig config,
        string? apiKey = null,
        string? authSource = null,
        string? description = null);

    void NormalizeCanonicalConfigurations(IList<ProviderConfig> configs);
}
