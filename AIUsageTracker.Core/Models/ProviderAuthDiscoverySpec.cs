namespace AIUsageTracker.Core.Models;

public sealed record ProviderAuthDiscoverySpec(
    string ProviderId,
    IReadOnlyCollection<string> DiscoveryEnvironmentVariables,
    IReadOnlyCollection<string> AuthIdentityCandidatePathTemplates,
    IReadOnlyCollection<ProviderAuthFileSchema> SessionAuthFileSchemas);
