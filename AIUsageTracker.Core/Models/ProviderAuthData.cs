namespace AIUsageTracker.Core.Models;

public sealed record ProviderAuthData(
    string AccessToken,
    string? AccountId = null,
    string? IdentityToken = null,
    string? SourcePath = null);
