// <copyright file="IMonitorLauncherClient.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Web.Services;

public interface IMonitorLauncherClient
{
    Task<MonitorAgentStatus> GetAgentStatusInfoAsync();

    Task<bool> EnsureAgentRunningAsync();

    Task<bool> StopAgentAsync();
}
