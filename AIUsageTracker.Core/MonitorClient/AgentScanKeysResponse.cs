// <copyright file="AgentScanKeysResponse.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentScanKeysResponse
{
    [JsonPropertyName("discovered")]
    public int Discovered { get; init; }

    [JsonPropertyName("refresh_queued")]
    public bool RefreshQueued { get; init; }

    [JsonPropertyName("configs")]
    public IReadOnlyList<ProviderConfig>? Configs { get; init; }
}
