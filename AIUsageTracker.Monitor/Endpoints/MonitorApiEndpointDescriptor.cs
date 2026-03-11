// <copyright file="MonitorApiEndpointDescriptor.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Endpoints;

internal sealed class MonitorApiEndpointDescriptor
{
    public string Route { get; init; } = string.Empty;

    public IReadOnlyList<string> Methods { get; init; } = [];
}
