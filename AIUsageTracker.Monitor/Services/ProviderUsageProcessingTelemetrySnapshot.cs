// <copyright file="ProviderUsageProcessingTelemetrySnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderUsageProcessingTelemetrySnapshot
{
    public long TotalProcessedEntries { get; init; }

    public long TotalAcceptedEntries { get; init; }

    public long TotalRejectedEntries { get; init; }

    public long InvalidIdentityCount { get; init; }

    public long InactiveProviderFilteredCount { get; init; }

    public long PlaceholderFilteredCount { get; init; }

    public long DetailContractAdjustedCount { get; init; }

    public long NormalizedCount { get; init; }

    public long PrivacyRedactedCount { get; init; }

    public DateTime? LastProcessedAtUtc { get; init; }

    public int LastRunTotalEntries { get; init; }

    public int LastRunAcceptedEntries { get; init; }
}
