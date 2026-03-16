// <copyright file="ProviderReliabilitySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public sealed class ProviderReliabilitySnapshot
{
    public bool IsAvailable { get; init; }

    public int SampleCount { get; init; }

    public int SuccessCount { get; init; }

    public int FailureCount { get; init; }

    public double FailureRatePercent { get; init; }

    public double AverageLatencyMs { get; init; }

    public double LastLatencyMs { get; init; }

    public DateTime? LastSuccessfulSyncUtc { get; init; }

    public DateTime? LastSeenUtc { get; init; }

    public string? Reason { get; init; }

    public static ProviderReliabilitySnapshot Unavailable(string reason)
    {
        return new ProviderReliabilitySnapshot
        {
            IsAvailable = false,
            Reason = reason,
        };
    }
}
