// <copyright file="AgentPipelineTelemetrySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentPipelineTelemetrySnapshot
{
    [JsonPropertyName("total_processed_entries")]
    public long TotalProcessedEntries { get; init; }

    [JsonPropertyName("total_accepted_entries")]
    public long TotalAcceptedEntries { get; init; }

    [JsonPropertyName("total_rejected_entries")]
    public long TotalRejectedEntries { get; init; }

    [JsonPropertyName("invalid_identity_count")]
    public long InvalidIdentityCount { get; init; }

    [JsonPropertyName("inactive_provider_filtered_count")]
    public long InactiveProviderFilteredCount { get; init; }

    [JsonPropertyName("placeholder_filtered_count")]
    public long PlaceholderFilteredCount { get; init; }

    [JsonPropertyName("detail_contract_adjusted_count")]
    public long DetailContractAdjustedCount { get; init; }

    [JsonPropertyName("normalized_count")]
    public long NormalizedCount { get; init; }

    [JsonPropertyName("privacy_redacted_count")]
    public long PrivacyRedactedCount { get; init; }

    [JsonPropertyName("last_processed_at_utc")]
    public DateTime? LastProcessedAtUtc { get; init; }

    [JsonPropertyName("last_run_total_entries")]
    public int LastRunTotalEntries { get; init; }

    [JsonPropertyName("last_run_accepted_entries")]
    public int LastRunAcceptedEntries { get; init; }
}
