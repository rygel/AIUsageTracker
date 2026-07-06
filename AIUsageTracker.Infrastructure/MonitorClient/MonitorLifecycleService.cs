// <copyright file="MonitorLifecycleService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Infrastructure.MonitorClient;

public sealed class MonitorLifecycleService
{
    private readonly MonitorLauncher _launcher;

    public MonitorLifecycleService(MonitorLauncher monitorLauncher)
    {
        this._launcher = monitorLauncher;
    }

    public Task<bool> StartAgentAsync()
    {
        return this._launcher.StartAgentAsync();
    }

    public Task<bool> StopAgentAsync()
    {
        return this._launcher.StopAgentAsync();
    }

    public Task<bool> EnsureAgentRunningAsync()
    {
        return this._launcher.EnsureAgentRunningAsync();
    }

    public Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default)
    {
        return this._launcher.WaitForAgentAsync(cancellationToken);
    }

    public Task<int> GetAgentPortAsync()
    {
        return this._launcher.GetAgentPortAsync();
    }

    public Task<bool> IsAgentRunningAsync()
    {
        return this._launcher.IsAgentRunningAsync();
    }

    public Task<(bool IsRunning, int Port)> IsAgentRunningWithPortAsync()
    {
        return this._launcher.IsAgentRunningWithPortAsync();
    }

    public async Task<MonitorAgentStatus> GetAgentStatusInfoAsync()
    {
        return await this._launcher.GetAgentStatusInfoAsync().ConfigureAwait(false);
    }

    public async Task<MonitorMetadataStatus> GetMonitorMetadataSnapshotAsync()
    {
        return await this._launcher.GetMonitorMetadataSnapshotAsync().ConfigureAwait(false);
    }
}
