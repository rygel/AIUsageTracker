namespace AIUsageTracker.Core.Models;

public sealed class ProviderDefinition
{
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

        ProviderId = providerId;
        DisplayName = displayName;
        PlanType = planType;
        IsQuotaBased = isQuotaBased;
        DefaultConfigType = defaultConfigType;
        AutoIncludeWhenUnconfigured = autoIncludeWhenUnconfigured;
        IncludeInWellKnownProviders = includeInWellKnownProviders;
        SupportsChildProviderIds = supportsChildProviderIds;
        DiscoveryEnvironmentVariables = NormalizeValues(discoveryEnvironmentVariables);
        RooConfigPropertyNames = NormalizeValues(rooConfigPropertyNames);
        NonPersistedProviderIds = NormalizeValues(nonPersistedProviderIds);
        VisibleDerivedProviderIds = NormalizeValues(visibleDerivedProviderIds);
        ExplicitApiKeyPrefixes = NormalizeValues(explicitApiKeyPrefixes);
        SessionAuthCanonicalProviderId = sessionAuthCanonicalProviderId;
        SessionAuthMigrationDescription = sessionAuthMigrationDescription;
        SettingsMode = settingsMode;
        UseSessionAuthStatusWhenQuotaBasedOrSessionToken = useSessionAuthStatusWhenQuotaBasedOrSessionToken;
        SessionStatusLabel = sessionStatusLabel;
        SessionIdentitySource = sessionIdentitySource;
        RefreshOnStartupWithCachedData = refreshOnStartupWithCachedData;
        IconAssetName = iconAssetName;
        FallbackBadgeColorHex = fallbackBadgeColorHex;
        FallbackBadgeInitial = fallbackBadgeInitial;
        AuthIdentityCandidatePathTemplates = NormalizeValues(authIdentityCandidatePathTemplates);
        SessionAuthFileSchemas = sessionAuthFileSchemas?
            .Where(schema => schema != null)
            .Distinct()
            .ToArray()
            ?? Array.Empty<ProviderAuthFileSchema>();

        var normalizedHandledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            providerId
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

        _handledProviderIds = normalizedHandledIds;
        HandledProviderIds = normalizedHandledIds.ToArray();
        DisplayNameOverrides = displayNameOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public bool HandlesProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        if (_handledProviderIds.Contains(providerId))
        {
            return true;
        }

        if (!SupportsChildProviderIds)
        {
            return false;
        }

        return _handledProviderIds.Any(handled =>
            providerId.StartsWith($"{handled}.", StringComparison.OrdinalIgnoreCase));
    }

    public string? ResolveDisplayName(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        if (DisplayNameOverrides.TryGetValue(providerId, out var mapped))
        {
            return mapped;
        }

        if (_handledProviderIds.Contains(providerId))
        {
            return DisplayName;
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
