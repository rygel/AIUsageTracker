// <copyright file="IMonitorLifecycleService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Core.Interfaces;

public interface IMonitorLifecycleService
{
    Task<bool> StartAgentAsync();

    Task<bool> StopAgentAsync();

    Task<bool> EnsureAgentRunningAsync();

    Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default);

    Task<int> GetAgentPortAsync();

    Task<bool> IsAgentRunningAsync();

    Task<(bool IsRunning, int Port)> IsAgentRunningWithPortAsync();

    Task<MonitorAgentStatus> GetAgentStatusInfoAsync();

    Task<MonitorMetadataStatus> GetMonitorMetadataSnapshotAsync();
}
