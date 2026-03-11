// <copyright file="UsageAnomalySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public sealed class UsageAnomalySnapshot
{
    public bool IsAvailable { get; init; }

    public bool HasAnomaly { get; init; }

    public string Direction { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public double BaselineRatePerDay { get; init; }

    public double LatestRatePerDay { get; init; }

    public double DeviationSigma { get; init; }

    public int SampleCount { get; init; }

    public DateTime? LastDetectedUtc { get; init; }

    public string? Reason { get; init; }

    public static UsageAnomalySnapshot Unavailable(string reason)
    {
        return new UsageAnomalySnapshot
        {
            IsAvailable = false,
            Reason = reason,
        };
    }
}
