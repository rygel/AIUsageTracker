// <copyright file="ProviderDefinition.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public sealed class ProviderDefinition
{
    private readonly HashSet<string> _handledProviderIds;

    public ProviderDefinition(
        string providerId,
        string displayName,
        PlanType planType,
        bool isQuotaBased,
        string defaultConfigType,
        bool autoIncludeWhenUnconfigured = false,
        bool includeInWellKnownProviders = false,
        IEnumerable<string>? handledProviderIds = null,
        IReadOnlyDictionary<string, string>? displayNameOverrides = null,
        ProviderFamilyMode familyMode = ProviderFamilyMode.Standalone,
        IEnumerable<string>? discoveryEnvironmentVariables = null,
        IEnumerable<string>? rooConfigPropertyNames = null,
        IEnumerable<string>? nonPersistedProviderIds = null,
        IEnumerable<string>? visibleDerivedProviderIds = null,
        IEnumerable<ProviderDerivedModelSelector>? derivedModelSelectors = null,
        IEnumerable<string>? explicitApiKeyPrefixes = null,
        string? sessionAuthCanonicalProviderId = null,
        string? sessionAuthMigrationDescription = null,
        ProviderSettingsMode settingsMode = ProviderSettingsMode.StandardApiKey,
        bool useSessionAuthStatusWhenQuotaBasedOrSessionToken = false,
        string? sessionStatusLabel = null,
        ProviderSessionIdentitySource sessionIdentitySource = ProviderSessionIdentitySource.None,
        bool refreshOnStartupWithCachedData = false,
        bool showInMainWindow = true,
        bool showInSettings = true,
        IEnumerable<string>? settingsAdditionalProviderIds = null,
        string? iconAssetName = null,
        string? fallbackBadgeColorHex = null,
        string? fallbackBadgeInitial = null,
        bool preferDisplayNameOverridesForDerivedProviderIds = false,
        string? aggregateDetailDisplaySuffix = null,
        bool supportsAccountIdentity = false,
        IEnumerable<string>? authIdentityCandidatePathTemplates = null,
        IEnumerable<ProviderAuthFileSchema>? sessionAuthFileSchemas = null,
        string? derivedModelDisplaySuffix = null)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id cannot be empty.", nameof(providerId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));
        }

        this.ProviderId = providerId;
        this.DisplayName = displayName;
        this.PlanType = planType;
        this.IsQuotaBased = isQuotaBased;
        this.DefaultConfigType = defaultConfigType;
        this.AutoIncludeWhenUnconfigured = autoIncludeWhenUnconfigured;
        this.IncludeInWellKnownProviders = includeInWellKnownProviders;
        this.FamilyMode = familyMode;
        this.DiscoveryEnvironmentVariables = NormalizeValues(discoveryEnvironmentVariables);
        this.RooConfigPropertyNames = NormalizeValues(rooConfigPropertyNames);
        this.NonPersistedProviderIds = NormalizeValues(nonPersistedProviderIds);
        this.VisibleDerivedProviderIds = NormalizeValues(visibleDerivedProviderIds);
        this.DerivedModelSelectors = NormalizeDerivedModelSelectors(derivedModelSelectors);
        this.ExplicitApiKeyPrefixes = NormalizeValues(explicitApiKeyPrefixes);
        this.SessionAuthCanonicalProviderId = sessionAuthCanonicalProviderId;
        this.SessionAuthMigrationDescription = sessionAuthMigrationDescription;
        this.SettingsMode = settingsMode;
        this.UseSessionAuthStatusWhenQuotaBasedOrSessionToken = useSessionAuthStatusWhenQuotaBasedOrSessionToken;
        this.SessionStatusLabel = sessionStatusLabel;
        this.SessionIdentitySource = sessionIdentitySource;
        this.RefreshOnStartupWithCachedData = refreshOnStartupWithCachedData;
        this.ShowInMainWindow = showInMainWindow;
        this.ShowInSettings = showInSettings;
        this.SettingsAdditionalProviderIds = NormalizeValues(settingsAdditionalProviderIds);
        this.IconAssetName = iconAssetName;
        this.FallbackBadgeColorHex = fallbackBadgeColorHex;
        this.FallbackBadgeInitial = fallbackBadgeInitial;
        this.PreferDisplayNameOverridesForDerivedProviderIds = preferDisplayNameOverridesForDerivedProviderIds;
        this.AggregateDetailDisplaySuffix = aggregateDetailDisplaySuffix;
        this.SupportsAccountIdentity = supportsAccountIdentity;
        this.AuthIdentityCandidatePathTemplates = NormalizeValues(authIdentityCandidatePathTemplates);
        this.SessionAuthFileSchemas = sessionAuthFileSchemas?
            .Where(schema => schema != null)
            .Distinct()
            .ToArray()
            ?? Array.Empty<ProviderAuthFileSchema>();
        this.DerivedModelDisplaySuffix = derivedModelDisplaySuffix;

        var normalizedHandledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            providerId,
        };

        if (handledProviderIds != null)
        {
            foreach (var handledId in handledProviderIds)
            {
                if (!string.IsNullOrWhiteSpace(handledId))
                {
                    normalizedHandledIds.Add(handledId);
                }
            }
        }

        this._handledProviderIds = normalizedHandledIds;
        this.HandledProviderIds = normalizedHandledIds.ToArray();
        this.DisplayNameOverrides = displayNameOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public string ProviderId { get; }

    public string DisplayName { get; }

    public PlanType PlanType { get; }

    public bool IsQuotaBased { get; }

    public string DefaultConfigType { get; }

    public bool AutoIncludeWhenUnconfigured { get; }

    public bool IncludeInWellKnownProviders { get; }

    public ProviderFamilyMode FamilyMode { get; }

    public bool SupportsChildProviderIds => ProviderFamilyPolicy.SupportsChildProviderIds(this.FamilyMode);

    public IReadOnlyCollection<string> HandledProviderIds { get; }

    public IReadOnlyDictionary<string, string> DisplayNameOverrides { get; }

    public IReadOnlyCollection<string> DiscoveryEnvironmentVariables { get; }

    public IReadOnlyCollection<string> RooConfigPropertyNames { get; }

    public IReadOnlyCollection<string> NonPersistedProviderIds { get; }

    public IReadOnlyCollection<string> VisibleDerivedProviderIds { get; }

    public IReadOnlyCollection<ProviderDerivedModelSelector> DerivedModelSelectors { get; }

    public IReadOnlyCollection<string> ExplicitApiKeyPrefixes { get; }

    public string? SessionAuthCanonicalProviderId { get; }

    public string? SessionAuthMigrationDescription { get; }

    public ProviderSettingsMode SettingsMode { get; }

    public bool UseSessionAuthStatusWhenQuotaBasedOrSessionToken { get; }

    public string? SessionStatusLabel { get; }

    public ProviderSessionIdentitySource SessionIdentitySource { get; }

    public bool RefreshOnStartupWithCachedData { get; }

    public bool CollapseDerivedChildrenInMainWindow =>
        ProviderFamilyPolicy.ShouldCollapseDerivedChildrenInMainWindow(this.FamilyMode);

    public bool ShowInMainWindow { get; }

    public bool ShowInSettings { get; }

    public IReadOnlyCollection<string> SettingsAdditionalProviderIds { get; }

    public string? IconAssetName { get; }

    public string? FallbackBadgeColorHex { get; }

    public string? FallbackBadgeInitial { get; }

    public bool PreferDisplayNameOverridesForDerivedProviderIds { get; }

    public bool RenderDetailsAsSyntheticChildrenInMainWindow =>
        ProviderFamilyPolicy.ShouldRenderSyntheticChildrenInMainWindow(this.FamilyMode);

    public string? AggregateDetailDisplaySuffix { get; }

    public bool SupportsAccountIdentity { get; }

    public IReadOnlyCollection<string> AuthIdentityCandidatePathTemplates { get; }

    public IReadOnlyCollection<ProviderAuthFileSchema> SessionAuthFileSchemas { get; }

    public bool UseChildProviderRowsForGroupedModels =>
        ProviderFamilyPolicy.UsesChildProviderRowsForGroupedModels(this.FamilyMode);

    public string? DerivedModelDisplaySuffix { get; }

    public bool HandlesProviderId(string providerId)
    {
        return ProviderFamilyPolicy.BelongsToProviderFamily(this.HandledProviderIds, providerId, this.FamilyMode);
    }

    public string? ResolveDisplayName(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        if (this.DisplayNameOverrides.TryGetValue(providerId, out var mapped))
        {
            return mapped;
        }

        if (this._handledProviderIds.Contains(providerId))
        {
            return this.DisplayName;
        }

        return null;
    }

    public ProviderAuthDiscoverySpec CreateAuthDiscoverySpec()
    {
        return new ProviderAuthDiscoverySpec(
            this.ProviderId,
            this.DiscoveryEnvironmentVariables,
            this.AuthIdentityCandidatePathTemplates,
            this.SessionAuthFileSchemas);
    }

    private static IReadOnlyCollection<string> NormalizeValues(IEnumerable<string>? values)
    {
        if (values == null)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyCollection<ProviderDerivedModelSelector> NormalizeDerivedModelSelectors(
        IEnumerable<ProviderDerivedModelSelector>? selectors)
    {
        if (selectors == null)
        {
            return Array.Empty<ProviderDerivedModelSelector>();
        }

        return selectors
            .Where(selector => selector != null)
            .GroupBy(selector => selector.DerivedProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }
}
