using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IProviderDiscoveryService
{
    Task<ProviderAuthData?> DiscoverAuthAsync(ProviderDefinition definition);

    string? GetEnvironmentVariable(string name);
}
