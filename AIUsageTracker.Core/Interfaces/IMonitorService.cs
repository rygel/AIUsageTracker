using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Core.Interfaces;

public interface IMonitorService
{
    string AgentUrl { get; set; }

    IReadOnlyList<string> LastAgentErrors { get; }

    Task RefreshAgentInfoAsync();

    Task RefreshPortAsync();

    Task<IReadOnlyList<ProviderUsage>> GetUsageAsync();

    Task<ProviderUsage?> GetUsageByProviderAsync(string providerId);

    Task<IReadOnlyList<ProviderUsage>> GetHistoryAsync(int limit = 100);

    Task<IReadOnlyList<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100);

    Task<bool> TriggerRefreshAsync();

    Task<IReadOnlyList<ProviderConfig>> GetConfigsAsync();

    Task<bool> SaveConfigAsync(ProviderConfig config);

    Task<bool> RemoveConfigAsync(string providerId);

    Task<AppPreferences> GetPreferencesAsync();

    Task<bool> SavePreferencesAsync(AppPreferences preferences);

    Task<bool> SendTestNotificationAsync();

    Task<AgentTestNotificationResult> SendTestNotificationDetailedAsync();

    Task<(int Count, IReadOnlyList<ProviderConfig> Configs)> ScanForKeysAsync();

    Task<bool> CheckHealthAsync();

    Task<AgentContractHandshakeResult> CheckApiContractAsync();

    Task<string> ExportDataAsync(string format);

    Task<string> GetHealthDetailsAsync();

    Task<string> GetDiagnosticsDetailsAsync();
}
