// <copyright file="MonitorActionResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public sealed class MonitorActionResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;
}
