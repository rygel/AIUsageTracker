// <copyright file="AgentGroupedUsageSnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentGroupedUsageSnapshot
{
    public string ContractVersion { get; set; } = MonitorApiContract.CurrentVersion;

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public IReadOnlyList<AgentGroupedProviderUsage> Providers { get; set; } = Array.Empty<AgentGroupedProviderUsage>();
}
