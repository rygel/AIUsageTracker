// <copyright file="QuotaProviderUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;

public class QuotaProviderUsage : ProviderUsage
{
    public double RequestsUsed { get; set; }

    public double RequestsAvailable { get; set; }

    [JsonPropertyName("used_percent")]
    public double UsedPercent { get; set; }

    [JsonIgnore]
    public double RemainingPercent => Math.Max(0, 100.0 - this.UsedPercent);

    public PlanType PlanType { get; set; } = PlanType.Usage;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCurrencyUsage { get; set; }

    public bool IsQuotaBased { get; set; }

    public bool DisplayAsFraction { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsStatusOnly { get; set; }

    public DateTime? NextResetTime { get; set; }

    [JsonIgnore]
    public double? UsagePerHour { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WindowKind WindowKind { get; set; } = WindowKind.None;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CardId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? PeriodDuration { get; set; }

    /// <summary>
    /// Gets or sets the number of rate-limit reset credits still available (e.g. Codex
    /// <c>rate_limit_reset_credits.available_count</c>). Null when the provider does not report it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResetCreditsAvailable { get; set; }

    /// <summary>
    /// Gets or sets the available reset-credit expiration timestamps (UTC), earliest first.
    /// The provider may cap detail rows, so this list can be shorter than <see cref="ResetCreditsAvailable"/>.
    /// Null when the provider reports only the count.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DateTime>? ResetCreditExpirationsUtc { get; set; }
}
