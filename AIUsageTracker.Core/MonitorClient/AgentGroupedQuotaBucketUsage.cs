// <copyright file="AgentGroupedQuotaBucketUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentGroupedQuotaBucketUsage
{
    public string BucketId { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    public double? UsedPercentage { get; set; }

    public double? RemainingPercentage { get; set; }

    public DateTime? NextResetTime { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The quota window kind for this bucket. Used to render dual progress bars on child cards.
    /// <see cref="WindowKind.None"/> when not applicable (e.g. summary/effective buckets).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WindowKind QuotaBucketKind { get; set; } = WindowKind.None;
}
