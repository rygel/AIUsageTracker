// <copyright file="IMonitorLauncher.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public interface IMonitorLauncher
{
    Task<int> GetAgentPortAsync();

    Task<bool> IsAgentRunningAsync();

    Task<(bool IsRunning, int Port)> IsAgentRunningWithPortAsync();

    Task<(bool IsRunning, int Port, bool HasMetadata)> GetAgentStatusAsync();

    Task<MonitorAgentStatus> GetAgentStatusInfoAsync();

    Task<MonitorInfo?> GetAndValidateMonitorInfoAsync();

    Task<bool> StartAgentAsync();

    Task<bool> EnsureAgentRunningAsync(CancellationToken cancellationToken = default);

    Task<bool> StopAgentAsync();

    Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default);

    Task InvalidateMonitorInfoAsync();

    Task<MonitorMetadataStatus> GetMonitorMetadataSnapshotAsync();
}
