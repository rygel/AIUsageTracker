// <copyright file="AgentGroupedQuotaBucketUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentGroupedQuotaBucketUsage
{
    public string BucketId { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    public double? UsedPercentage { get; set; }

    public double? RemainingPercentage { get; set; }

    public DateTime? NextResetTime { get; set; }

    public string Description { get; set; } = string.Empty;
}
