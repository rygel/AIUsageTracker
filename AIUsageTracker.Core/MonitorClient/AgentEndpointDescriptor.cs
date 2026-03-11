// <copyright file="AgentEndpointDescriptor.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentEndpointDescriptor
{
    [JsonPropertyName("route")]
    public string Route { get; init; } = string.Empty;

    [JsonPropertyName("methods")]
    public IReadOnlyList<string> Methods { get; init; } = [];
}
