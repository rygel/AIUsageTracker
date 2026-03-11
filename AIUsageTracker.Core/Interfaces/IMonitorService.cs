// <copyright file="IMonitorService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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

    Task<bool> SendTestNotificationAsync();

    Task<MonitorActionResult> SendTestNotificationDetailedAsync();

    Task<AgentScanKeysResult> ScanForKeysAsync();

    Task<MonitorActionResult> CheckProviderAsync(string providerId);

    Task<bool> CheckHealthAsync();

    Task<MonitorHealthSnapshot?> GetHealthSnapshotAsync();

    Task<AgentContractHandshakeResult> CheckApiContractAsync();

    Task<string> ExportDataAsync(string format);

    Task<Stream?> ExportDataAsync(string format, int days);

    Task<AgentDiagnosticsSnapshot?> GetDiagnosticsSnapshotAsync();
}
