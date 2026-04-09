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

    Task<AgentGroupedUsageSnapshot?> GetGroupedUsageAsync();

    Task<ProviderUsage?> GetUsageByProviderAsync(string providerId);

    Task<IReadOnlyList<ProviderUsage>> GetHistoryAsync(int limit = 100);

    Task<IReadOnlyList<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100);

    Task<bool> TriggerRefreshAsync();

    Task<IReadOnlyList<ProviderConfig>> GetConfigsAsync();

    Task<bool> SaveConfigAsync(ProviderConfig config);

    Task<bool> RemoveConfigAsync(string providerId);

    /// <summary>
    /// Clears the cached grouped-usage ETag so the next <see cref="GetGroupedUsageAsync"/>
    /// call fetches fresh data instead of returning a stale 304 response.
    /// Call after <see cref="SaveConfigAsync"/> or <see cref="RemoveConfigAsync"/>
    /// to ensure config changes are reflected immediately.
    /// </summary>
    void InvalidateGroupedUsageCache();

    Task<bool> SendTestNotificationAsync();

    Task<MonitorActionResult> SendTestNotificationDetailedAsync();

    Task<AgentScanKeysResult> ScanForKeysAsync();

    Task<MonitorActionResult> CheckProviderAsync(string providerId);

    Task<bool> CheckHealthAsync();

    /// <summary>
    /// Checks if the monitor is healthy, with a custom timeout for fast-fail scenarios.
    /// </summary>
    Task<bool> CheckHealthAsync(TimeSpan timeout);

    Task<MonitorHealthSnapshot?> GetHealthSnapshotAsync();

    Task<AgentContractHandshakeResult> CheckApiContractAsync();

    Task<string> ExportDataAsync(string format);

    Task<Stream?> ExportDataAsync(string format, int days);

    Task<AgentDiagnosticsSnapshot?> GetDiagnosticsSnapshotAsync();
}
