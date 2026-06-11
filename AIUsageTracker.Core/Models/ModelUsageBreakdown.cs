// <copyright file="ModelUsageBreakdown.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public class ModelUsageBreakdown
{
    public string ModelName { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public int SampleCount { get; set; }

    public double TotalUsed { get; set; }

    public double AvgUsedPercent { get; set; }
}
