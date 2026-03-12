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

    public string Description { get; set; } = string.Empty;

    public double? EffectiveUsedPercentage { get; set; }

    public double? EffectiveRemainingPercentage { get; set; }

    public DateTime? EffectiveNextResetTime { get; set; }

    public string? EffectiveDescription { get; set; }

    public IReadOnlyList<AgentGroupedQuotaBucketUsage> QuotaBuckets { get; set; } = Array.Empty<AgentGroupedQuotaBucketUsage>();
}
