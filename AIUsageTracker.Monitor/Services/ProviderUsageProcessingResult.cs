// <copyright file="ProviderUsageProcessingResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderUsageProcessingResult
{
    public IReadOnlyList<ProviderUsage> Usages { get; init; } = [];

    public int InvalidIdentityCount { get; init; }

    public int InactiveProviderFilteredCount { get; init; }

    public int PlaceholderFilteredCount { get; init; }

    public int DetailContractAdjustedCount { get; init; }

    public int NormalizedCount { get; init; }

    public int PrivacyRedactedCount { get; init; }
}
