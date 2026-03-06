namespace AIUsageTracker.Core.Models;

public sealed record ProviderAuthFileSchema(
    string RootProperty,
    string AccessTokenProperty,
    string? AccountIdProperty = null);
