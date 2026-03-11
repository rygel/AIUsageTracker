// <copyright file="MonitorAgentStatus.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public sealed class MonitorAgentStatus
{
    public bool IsRunning { get; init; }

    public int Port { get; init; }

    public bool HasMetadata { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Error { get; init; }
}
