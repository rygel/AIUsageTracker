namespace AIUsageTracker.Core.Interfaces;

using AIUsageTracker.Core.Models;

public interface IProviderDiscoveryService
{
    Task<ProviderAuthData?> DiscoverAuthAsync(ProviderDefinition definition);

    string? GetEnvironmentVariable(string name);
}
