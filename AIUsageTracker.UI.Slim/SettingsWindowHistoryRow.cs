// <copyright file="SettingsWindowHistoryRow.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed class SettingsWindowHistoryRow
{
    public string ProviderName { get; init; } = string.Empty;

    public double UsagePercentage { get; init; }

    public double Used { get; init; }

    public double Limit { get; init; }

    public string PlanType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateTime FetchedAt { get; init; }
}
