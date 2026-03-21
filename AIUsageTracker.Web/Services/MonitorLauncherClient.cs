// <copyright file="MonitorLauncherClient.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Web.Services;

public sealed class MonitorLauncherClient : IMonitorLauncherClient
{
    private readonly IMonitorLauncher _monitorLauncher;

    public MonitorLauncherClient(IMonitorLauncher monitorLauncher)
    {
        this._monitorLauncher = monitorLauncher;
    }

    public Task<MonitorAgentStatus> GetAgentStatusInfoAsync()
    {
        return this._monitorLauncher.GetAgentStatusInfoAsync();
    }

    public Task<bool> EnsureAgentRunningAsync()
    {
        return this._monitorLauncher.EnsureAgentRunningAsync();
    }

    public Task<bool> StopAgentAsync()
    {
        return this._monitorLauncher.StopAgentAsync();
    }
}
