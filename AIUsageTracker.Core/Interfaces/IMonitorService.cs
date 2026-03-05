using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Core.Interfaces;

public interface IMonitorService
{
    string AgentUrl { get; set; }
    IReadOnlyList<string> LastAgentErrors { get; }
    Task RefreshAgentInfoAsync();
    Task RefreshPortAsync();
    Task<List<ProviderUsage>> GetUsageAsync();
    Task<ProviderUsage?> GetUsageByProviderAsync(string providerId);
    Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100);
    Task<List<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100);
    Task<bool> TriggerRefreshAsync();
    Task<List<ProviderConfig>> GetConfigsAsync();
    Task<bool> SaveConfigAsync(ProviderConfig config);
    Task<bool> RemoveConfigAsync(string providerId);
    Task<AppPreferences> GetPreferencesAsync();
    Task<bool> SavePreferencesAsync(AppPreferences preferences);
    Task<bool> SendTestNotificationAsync();
    Task<AgentTestNotificationResult> SendTestNotificationDetailedAsync();
    Task<(int count, List<ProviderConfig> configs)> ScanForKeysAsync();
    Task<bool> CheckHealthAsync();
    Task<AgentContractHandshakeResult> CheckApiContractAsync();
    Task<string> ExportDataAsync(string format);
    Task<string> GetHealthDetailsAsync();
    Task<string> GetDiagnosticsDetailsAsync();
}
