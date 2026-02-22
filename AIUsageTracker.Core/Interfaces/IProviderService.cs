using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IProviderService
{
    string ProviderId { get; }
    Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null);
}



