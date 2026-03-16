// <copyright file="ProviderUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;

public class ProviderUsage
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public double RequestsUsed { get; set; }

    public double RequestsAvailable { get; set; }

    /// <summary>
    /// Gets or sets the percentage of quota/budget consumed (0–100), regardless of whether the provider is quota-based.
    /// </summary>
    [JsonPropertyName("used_percent")]
    public double UsedPercent { get; set; }

    /// <summary>
    /// Gets the percentage of quota/budget remaining (0–100), regardless of whether the provider is quota-based.
    /// </summary>
    [JsonIgnore]
    public double RemainingPercent => Math.Max(0, 100.0 - this.UsedPercent);

    public PlanType PlanType { get; set; } = PlanType.Usage;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCurrencyUsage { get; set; }

    public bool IsQuotaBased { get; set; }

    public bool DisplayAsFraction { get; set; } // Explicitly request "X / Y" display format

    public bool IsAvailable { get; set; } = true;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(JsonStringEnumConverter<ProviderUsageState>))]
    public ProviderUsageState State { get; set; } = ProviderUsageState.Available;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsStatusOnly { get; set; }

    public string AuthSource { get; set; } = string.Empty;

    /// <summary>
    /// For child/derived provider rows, the provider_id of the parent.
    /// Null for top-level (non-derived) providers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentProviderId { get; set; }

    public IReadOnlyList<ProviderUsageDetail>? Details { get; set; }

    // Temporary property for database serialization - not serialized to JSON
    [JsonIgnore]
    public string? DetailsJson { get; set; }

    public string AccountName { get; set; } = string.Empty;

    public string ConfigKey { get; set; } = string.Empty;

    public DateTime? NextResetTime { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public double ResponseLatencyMs { get; set; }

    /// <summary>
    /// Raw JSON response from the provider API. Intentional audit trail — stored in the database
    /// and privacy-redacted in the processing pipeline when privacy mode is enabled.
    /// Not surfaced in the UI; used for diagnostics and post-hoc debugging.
    /// </summary>
    public string? RawJson { get; set; }

    public int HttpStatus { get; set; } = 200;

    public UpstreamResponseValidity UpstreamResponseValidity { get; set; } = UpstreamResponseValidity.Unknown;

    public string UpstreamResponseNote { get; set; } = string.Empty;
}
