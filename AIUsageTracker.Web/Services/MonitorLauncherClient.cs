// <copyright file="MonitorLauncherClient.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.MonitorClient;

namespace AIUsageTracker.Web.Services;

public sealed class MonitorLauncherClient : IMonitorLauncherClient
{
    private readonly MonitorLauncher _monitorLauncher;

    public MonitorLauncherClient(MonitorLauncher monitorLauncher)
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
