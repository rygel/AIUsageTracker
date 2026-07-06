// <copyright file="LatencyDataPoint.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public class LatencyDataPoint
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public double AvgLatencyMs { get; set; }

    public double MaxLatencyMs { get; set; }
}
