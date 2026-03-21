// <copyright file="IMonitorStartupOrchestrator.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

public interface IMonitorStartupOrchestrator
{
    Task<MonitorStartupOrchestrationResult> EnsureMonitorReadyAsync(Func<string, StatusType, Task> reportStatusAsync);
}
