using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IProviderDiscoveryService
{
    Task<ProviderAuthData?> DiscoverAuthAsync(ProviderAuthDiscoverySpec discoverySpec);

    string? GetEnvironmentVariable(string name);
}
