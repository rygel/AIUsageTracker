// <copyright file="AgentHealthSnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentHealthSnapshot
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; init; }

    [JsonPropertyName("port")]
    public int? Port { get; init; }

    [JsonPropertyName("process_id")]
    public int? ProcessId { get; init; }

    [JsonPropertyName("agent_version")]
    public string? AgentVersion { get; init; }

    [JsonPropertyName("api_contract_version")]
    public string? ApiContractVersion { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; init; }

    public string? ResolveApiContractVersion()
    {
        return this.ApiContractVersion
            ?? this.TryReadString("apiContractVersion");
    }

    public string? ResolveAgentVersion()
    {
        return this.AgentVersion
            ?? this.TryReadString("agentVersion")
            ?? this.TryReadString("version");
    }

    private string? TryReadString(string propertyName)
    {
        if (this.ExtraProperties == null || !this.ExtraProperties.TryGetValue(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }
}
