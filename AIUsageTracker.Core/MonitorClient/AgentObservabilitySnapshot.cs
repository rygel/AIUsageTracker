// <copyright file="AgentObservabilitySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentObservabilitySnapshot
{
    [JsonPropertyName("activity_source_names")]
    public IReadOnlyList<string> ActivitySourceNames { get; init; } = [];
}
