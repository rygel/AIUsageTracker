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
        bool supportsChildProviderIds = false,
        IEnumerable<string>? discoveryEnvironmentVariables = null,
        IEnumerable<string>? rooConfigPropertyNames = null,
        IEnumerable<string>? nonPersistedProviderIds = null,
        IEnumerable<string>? visibleDerivedProviderIds = null,
        IEnumerable<string>? explicitApiKeyPrefixes = null,
        string? sessionAuthCanonicalProviderId = null,
        string? sessionAuthMigrationDescription = null,
        ProviderSettingsMode settingsMode = ProviderSettingsMode.StandardApiKey,
        bool useSessionAuthStatusWhenQuotaBasedOrSessionToken = false,
        string? sessionStatusLabel = null,
        ProviderSessionIdentitySource sessionIdentitySource = ProviderSessionIdentitySource.None,
        bool refreshOnStartupWithCachedData = false,
        string? iconAssetName = null,
        string? fallbackBadgeColorHex = null,
        string? fallbackBadgeInitial = null,
        IEnumerable<string>? authIdentityCandidatePathTemplates = null,
        IEnumerable<ProviderAuthFileSchema>? sessionAuthFileSchemas = null)
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
        this.SupportsChildProviderIds = supportsChildProviderIds;
        this.DiscoveryEnvironmentVariables = NormalizeValues(discoveryEnvironmentVariables);
        this.RooConfigPropertyNames = NormalizeValues(rooConfigPropertyNames);
        this.NonPersistedProviderIds = NormalizeValues(nonPersistedProviderIds);
        this.VisibleDerivedProviderIds = NormalizeValues(visibleDerivedProviderIds);
        this.ExplicitApiKeyPrefixes = NormalizeValues(explicitApiKeyPrefixes);
        this.SessionAuthCanonicalProviderId = sessionAuthCanonicalProviderId;
        this.SessionAuthMigrationDescription = sessionAuthMigrationDescription;
        this.SettingsMode = settingsMode;
        this.UseSessionAuthStatusWhenQuotaBasedOrSessionToken = useSessionAuthStatusWhenQuotaBasedOrSessionToken;
        this.SessionStatusLabel = sessionStatusLabel;
        this.SessionIdentitySource = sessionIdentitySource;
        this.RefreshOnStartupWithCachedData = refreshOnStartupWithCachedData;
        this.IconAssetName = iconAssetName;
        this.FallbackBadgeColorHex = fallbackBadgeColorHex;
        this.FallbackBadgeInitial = fallbackBadgeInitial;
        this.AuthIdentityCandidatePathTemplates = NormalizeValues(authIdentityCandidatePathTemplates);
        this.SessionAuthFileSchemas = sessionAuthFileSchemas?
            .Where(schema => schema != null)
            .Distinct()
            .ToArray()
            ?? Array.Empty<ProviderAuthFileSchema>();

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

    public bool SupportsChildProviderIds { get; }

    public IReadOnlyCollection<string> HandledProviderIds { get; }

    public IReadOnlyDictionary<string, string> DisplayNameOverrides { get; }

    public IReadOnlyCollection<string> DiscoveryEnvironmentVariables { get; }

    public IReadOnlyCollection<string> RooConfigPropertyNames { get; }

    public IReadOnlyCollection<string> NonPersistedProviderIds { get; }

    public IReadOnlyCollection<string> VisibleDerivedProviderIds { get; }

    public IReadOnlyCollection<string> ExplicitApiKeyPrefixes { get; }

    public string? SessionAuthCanonicalProviderId { get; }

    public string? SessionAuthMigrationDescription { get; }

    public ProviderSettingsMode SettingsMode { get; }

    public bool UseSessionAuthStatusWhenQuotaBasedOrSessionToken { get; }

    public string? SessionStatusLabel { get; }

    public ProviderSessionIdentitySource SessionIdentitySource { get; }

    public bool RefreshOnStartupWithCachedData { get; }

    public string? IconAssetName { get; }

    public string? FallbackBadgeColorHex { get; }

    public string? FallbackBadgeInitial { get; }

    public IReadOnlyCollection<string> AuthIdentityCandidatePathTemplates { get; }

    public IReadOnlyCollection<ProviderAuthFileSchema> SessionAuthFileSchemas { get; }

    public bool HandlesProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        if (this._handledProviderIds.Contains(providerId))
        {
            return true;
        }

        if (!this.SupportsChildProviderIds)
        {
            return false;
        }

        return this._handledProviderIds.Any(handled =>
            providerId.StartsWith($"{handled}.", StringComparison.OrdinalIgnoreCase));
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
}
