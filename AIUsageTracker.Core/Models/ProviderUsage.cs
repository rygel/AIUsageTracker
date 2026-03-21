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
    /// Gets or sets for child/derived provider rows, the provider_id of the parent.
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
    /// Gets or sets raw JSON response from the provider API. Intentional audit trail — stored in the database
    /// and privacy-redacted in the processing pipeline when privacy mode is enabled.
    /// Not surfaced in the UI; used for diagnostics and post-hoc debugging.
    /// </summary>
    public string? RawJson { get; set; }

    public int HttpStatus { get; set; } = 200;

    public UpstreamResponseValidity UpstreamResponseValidity { get; set; } = UpstreamResponseValidity.Unknown;

    public string UpstreamResponseNote { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether true when the row was retrieved from the database but is older than the staleness
    /// threshold, meaning no successful refresh has occurred recently. The UI should
    /// visually distinguish stale entries so users know they are looking at cached data.
    /// </summary>
    public bool IsStale { get; set; }

    /// <summary>
    /// Gets or sets derived burn rate: requests consumed per hour, computed from the delta between the
    /// latest row and the row closest to one hour ago. Null when there is insufficient
    /// history or when the counter was reset (delta would be negative).
    /// Not stored in the database — computed on read and never serialised.
    /// </summary>
    [JsonIgnore]
    public double? UsagePerHour { get; set; }

    /// <summary>
    /// Gets or sets duration of the primary rolling quota window (e.g. 7 days for a weekly quota).
    /// Set by the display layer when synthesising child provider rows from aggregate details,
    /// or directly by the provider when the usage row represents a single rolling window.
    /// Null when no rolling-window period duration is known.
    /// Not stored in the database — derived on read and never serialised.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? PeriodDuration { get; set; }

    public (UpstreamResponseValidity Validity, string Note) EvaluateUpstreamResponseValidity()
    {
        if (this.UpstreamResponseValidity != UpstreamResponseValidity.Unknown)
        {
            return (
                this.UpstreamResponseValidity,
                string.IsNullOrWhiteSpace(this.UpstreamResponseNote)
                    ? GetDefaultUpstreamResponseNote(this.UpstreamResponseValidity, this.HttpStatus)
                    : this.UpstreamResponseNote);
        }

        // Typed state short-circuits before any description heuristics.
        if (this.State == ProviderUsageState.Missing || this.State == ProviderUsageState.Unavailable)
        {
            return (UpstreamResponseValidity.NotAttempted, "Upstream call was not attempted");
        }

        if (this.State == ProviderUsageState.Error)
        {
            return (UpstreamResponseValidity.Invalid, "Provider reported an error");
        }

        var hasHttpStatus = this.HttpStatus is >= 100 and <= 599;
        if (hasHttpStatus)
        {
            if (this.HttpStatus is >= 200 and <= 299)
            {
                return (UpstreamResponseValidity.Valid, $"HTTP {this.HttpStatus}");
            }

            return (UpstreamResponseValidity.Invalid, $"HTTP {this.HttpStatus}");
        }

        if (!string.IsNullOrWhiteSpace(this.RawJson))
        {
            return this.IsAvailable
                ? (UpstreamResponseValidity.Valid, "Payload captured (no HTTP status)")
                : (UpstreamResponseValidity.Invalid, "Payload captured but usage is unavailable");
        }

        return this.IsAvailable
            ? (UpstreamResponseValidity.Unknown, "No upstream validation metadata")
            : (UpstreamResponseValidity.NotAttempted, "Unavailable without upstream response metadata");
    }

    private static string GetDefaultUpstreamResponseNote(UpstreamResponseValidity validity, int httpStatus)
    {
        return validity switch
        {
            UpstreamResponseValidity.Valid when httpStatus is >= 100 and <= 599 => $"HTTP {httpStatus}",
            UpstreamResponseValidity.Invalid when httpStatus is >= 100 and <= 599 => $"HTTP {httpStatus}",
            UpstreamResponseValidity.NotAttempted => "Upstream call was not attempted",
            UpstreamResponseValidity.Valid => "Upstream response valid",
            UpstreamResponseValidity.Invalid => "Upstream response invalid",
            _ => "Unknown upstream response validity",
        };
    }
}
