// <copyright file="AgentGroupedModelUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentGroupedModelUsage
{
    public string ModelId { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public double? UsedPercentage { get; set; }

    public double? RemainingPercentage { get; set; }

    public DateTime? NextResetTime { get; set; }

    /// <summary>
    /// Gets or sets the number of rate-limit reset credits still available (e.g. Codex
    /// <c>rate_limit_reset_credits.available_count</c>). Null when not reported.
    /// </summary>
    public int? ResetCreditsAvailable { get; set; }

    /// <summary>
    /// Gets or sets the per-reset expiration timestamps (UTC). Each entry corresponds to one
    /// available reset credit; the earliest entry is the next credit that becomes usable.
    /// </summary>
    public IReadOnlyList<DateTime>? ResetCreditExpirationsUtc { get; set; }

    public string Description { get; set; } = string.Empty;

    public double? EffectiveUsedPercentage { get; set; }

    public double? EffectiveRemainingPercentage { get; set; }

    public DateTime? EffectiveNextResetTime { get; set; }

    public string? EffectiveDescription { get; set; }

    public IReadOnlyList<AgentGroupedQuotaBucketUsage> QuotaBuckets { get; set; } = Array.Empty<AgentGroupedQuotaBucketUsage>();
}
