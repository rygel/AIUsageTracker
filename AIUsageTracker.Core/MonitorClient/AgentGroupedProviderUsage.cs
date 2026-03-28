// <copyright file="AgentGroupedProviderUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentGroupedProviderUsage
{
    /// <summary>
    /// Gets or sets provider-level quota window details (e.g. Kimi's Weekly Limit + 5h Limit).
    /// Populated when the provider has QuotaWindow details that are not scoped to
    /// a specific model. Used by the UI to render dual progress bars on the parent card.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public PlanType PlanType { get; set; } = PlanType.Usage;

    public bool IsQuotaBased { get; set; }

    public double RequestsUsed { get; set; }

    public double RequestsAvailable { get; set; }

    public double UsedPercent { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public DateTime? NextResetTime { get; set; }

    public IReadOnlyList<AgentGroupedModelUsage> Models { get; set; } = Array.Empty<AgentGroupedModelUsage>();

    /// <summary>
    /// Gets or sets the flat provider usage cards for this group.
    /// Cards with WindowKind != None drive quota window bars.
    /// Cards with ModelName != null drive per-model rows.
    /// </summary>
    public IReadOnlyList<ProviderUsage> ProviderDetails { get; set; } = Array.Empty<ProviderUsage>();
}
