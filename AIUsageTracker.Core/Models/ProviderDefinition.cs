// <copyright file="ProviderDefinition.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public sealed class ProviderDefinition
{
    private HashSet<string>? _handledProviderIdsSet;

    public ProviderDefinition(
        string providerId,
        string displayName,
        PlanType planType,
        bool isQuotaBased)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id cannot be empty.", nameof(providerId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));
        }

        this.ProviderId = providerId.Trim();
        this.DisplayName = displayName.Trim();
        this.PlanType = planType;
        this.IsQuotaBased = isQuotaBased;
    }

    public string ProviderId { get; }

    public string DisplayName { get; }

    public PlanType PlanType { get; }

    public bool IsQuotaBased { get; }

    public ProviderFamilyMode FamilyMode { get; init; } = ProviderFamilyMode.Standalone;

    public bool SupportsChildProviderIds => ProviderFamilyPolicy.SupportsChildProviderIds(this.FamilyMode);

    /// <summary>
    /// Gets additional provider IDs (beyond ProviderId itself) that this provider handles.
    /// Use this to declare aliases; ProviderId is always included automatically.
    /// </summary>
    public IReadOnlyCollection<string> AdditionalHandledProviderIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> HandledProviderIds => this.HandledProviderIdsSet;

    public IReadOnlyDictionary<string, string> DisplayNameOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> DiscoveryEnvironmentVariables { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> RooConfigPropertyNames { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> NonPersistedProviderIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> VisibleDerivedProviderIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<ProviderDerivedModelSelector> DerivedModelSelectors { get; init; } = Array.Empty<ProviderDerivedModelSelector>();

    /// <summary>
    /// Gets model IDs that are consumed by the parent card (e.g. progress bars) and must not
    /// appear as dynamic child rows even when other VisibleDerivedProviderIds are declared.
    /// </summary>
    public IReadOnlyCollection<string> ExcludedDerivedModelIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ExplicitApiKeyPrefixes { get; init; } = Array.Empty<string>();

    public ProviderSettingsMode SettingsMode { get; init; } = ProviderSettingsMode.StandardApiKey;

    public bool UseSessionAuthStatusWhenQuotaBasedOrSessionToken { get; init; }

    public string? SessionStatusLabel { get; init; }

    public ProviderSessionIdentitySource SessionIdentitySource { get; init; } = ProviderSessionIdentitySource.None;

    public bool RefreshOnStartupWithCachedData { get; init; }

    public bool ShowInMainWindow { get; init; } = true;

    public bool ShowInSettings { get; init; } = true;

    public IReadOnlyCollection<string> SettingsAdditionalProviderIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> CoReportedProviderIds { get; init; } = Array.Empty<string>();

    public string? IconAssetName { get; init; }

    public string? BadgeColorHex { get; init; }

    public string? BadgeInitial { get; init; }

    public bool SupportsAccountIdentity { get; init; }

    public IReadOnlyCollection<string> AuthIdentityCandidatePathTemplates { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<ProviderAuthFileSchema> SessionAuthFileSchemas { get; init; } = Array.Empty<ProviderAuthFileSchema>();

    public IReadOnlyCollection<string> SessionIdentityProfileRootProperties { get; init; } = Array.Empty<string>();

    public bool UseChildProviderRowsForGroupedModels =>
        ProviderFamilyPolicy.UsesChildProviderRowsForGroupedModels(this.FamilyMode);

    public string? DerivedModelDisplaySuffix { get; init; }

    public bool IsTooltipOnly { get; init; }

    public bool IsStatusOnly { get; init; }

    public bool IsCurrencyUsage { get; init; }

    public bool DisplayAsFraction { get; init; }

    /// <summary>
    /// Gets a value indicating whether flat model cards for this provider should show the provider display name as a
    /// prefix (e.g. "Claude Code (Current Session)"). Use when card names are generic and need provider
    /// context to be meaningful.
    /// </summary>
    public bool FlatCardShowProviderPrefix { get; init; }

    public IReadOnlyList<QuotaWindowDefinition> QuotaWindows { get; init; } = Array.Empty<QuotaWindowDefinition>();

    // Computed lazily: ProviderId + AdditionalHandledProviderIds
    private HashSet<string> HandledProviderIdsSet => this._handledProviderIdsSet ??=
        new HashSet<string>(this.AdditionalHandledProviderIds.Prepend(this.ProviderId), StringComparer.OrdinalIgnoreCase);

    public bool HasDisplayableDerivedProviders =>
        ProviderFamilyPolicy.HasDisplayableDerivedProviders(this.VisibleDerivedProviderIds, this.FamilyMode);

    public bool HandlesProviderId(string providerId)
    {
        return ProviderFamilyPolicy.BelongsToProviderFamily(this.HandledProviderIds, providerId, this.FamilyMode);
    }

    public bool IsChildProviderId(string candidateProviderId)
    {
        return ProviderFamilyPolicy.IsChildProviderId(this.HandledProviderIds, candidateProviderId, this.FamilyMode);
    }

    public bool TryGetChildProviderKey(string candidateProviderId, out string childProviderKey)
    {
        return ProviderFamilyPolicy.TryGetChildProviderKey(
            this.HandledProviderIds, candidateProviderId, this.FamilyMode, out childProviderKey);
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

        if (this.HandledProviderIdsSet.Contains(providerId))
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

    public ProviderConfig CreateDefaultConfig(
        string? providerId = null,
        string? apiKey = null,
        string? authSource = null,
        string? description = null)
    {
        return new ProviderConfig
        {
            ProviderId = string.IsNullOrWhiteSpace(providerId) ? this.ProviderId : providerId,
            ApiKey = apiKey ?? string.Empty,
            AuthSource = authSource ?? AuthSource.Unknown,
            Description = description,
        };
    }
}
