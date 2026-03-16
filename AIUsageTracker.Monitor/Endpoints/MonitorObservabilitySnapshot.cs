// <copyright file="MonitorObservabilitySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Endpoints;

internal sealed class MonitorObservabilitySnapshot
{
    public IReadOnlyList<string> ActivitySourceNames { get; init; } = [];
}
