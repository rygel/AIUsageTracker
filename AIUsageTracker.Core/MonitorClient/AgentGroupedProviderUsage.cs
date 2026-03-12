// <copyright file="AgentGroupedProviderUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentGroupedProviderUsage
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public PlanType PlanType { get; set; } = PlanType.Usage;

    public bool IsQuotaBased { get; set; }

    public double RequestsUsed { get; set; }

    public double RequestsAvailable { get; set; }

    public double RequestsPercentage { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public DateTime? NextResetTime { get; set; }

    public int ModelCount { get; set; }

    public IReadOnlyList<AgentGroupedModelUsage> Models { get; set; } = Array.Empty<AgentGroupedModelUsage>();
}
