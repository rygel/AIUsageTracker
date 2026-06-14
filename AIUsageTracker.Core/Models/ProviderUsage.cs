// <copyright file="ProviderUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;

[JsonDerivedType(typeof(QuotaProviderUsage), typeDiscriminator: "quota")]
[JsonDerivedType(typeof(WindowedProviderUsage), typeDiscriminator: "windowed")]
[JsonDerivedType(typeof(ModelScopedProviderUsage), typeDiscriminator: "model")]
[JsonDerivedType(typeof(StatusProviderUsage), typeDiscriminator: "status")]
public abstract class ProviderUsage
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public bool IsAvailable { get; set; } = true;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(JsonStringEnumConverter<ProviderUsageState>))]
    public ProviderUsageState State { get; set; } = ProviderUsageState.Available;

    public string AuthSource { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTooltipOnly { get; set; }

    public string AccountName { get; set; } = string.Empty;

    public string ConfigKey { get; set; } = string.Empty;

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
    /// Gets or sets structured failure context attached by the provider when an upstream HTTP or
    /// transport failure occurred. Not stored in the database and never serialised — intended for
    /// diagnostics, monitor resilience decisions, and future observability surfaces.
    /// Null when the usage row represents a successful fetch or when failure context was not captured.
    /// </summary>
    [JsonIgnore]
    public HttpFailureContext? FailureContext { get; set; }

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
                return (UpstreamResponseValidity.Valid, $"HTTP {this.HttpStatus.ToString(CultureInfo.InvariantCulture)}");
            }

            return (UpstreamResponseValidity.Invalid, $"HTTP {this.HttpStatus.ToString(CultureInfo.InvariantCulture)}");
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
            UpstreamResponseValidity.Valid when httpStatus is >= 100 and <= 599 => $"HTTP {httpStatus.ToString(CultureInfo.InvariantCulture)}",
            UpstreamResponseValidity.Invalid when httpStatus is >= 100 and <= 599 => $"HTTP {httpStatus.ToString(CultureInfo.InvariantCulture)}",
            UpstreamResponseValidity.NotAttempted => "Upstream call was not attempted",
            UpstreamResponseValidity.Valid => "Upstream response valid",
            UpstreamResponseValidity.Invalid => "Upstream response invalid",
            _ => "Unknown upstream response validity",
        };
    }
}
