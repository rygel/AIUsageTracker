// <copyright file="MonitorLauncherClient.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Web.Services;

public sealed class MonitorLauncherClient : IMonitorLauncherClient
{
    public Task<MonitorAgentStatus> GetAgentStatusInfoAsync()
    {
        return MonitorLauncher.GetAgentStatusInfoAsync();
    }

    public Task<bool> EnsureAgentRunningAsync()
    {
        return MonitorLauncher.EnsureAgentRunningAsync();
    }

    public Task<bool> StopAgentAsync()
    {
        return MonitorLauncher.StopAgentAsync();
    }
}
