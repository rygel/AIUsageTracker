// <copyright file="ProviderCardPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderCardPresentation(
    bool IsMissing,
    bool IsUnknown,
    bool IsError,
    bool ShouldHaveProgress,
    bool SuppressSingleResetTime,
    double UsedPercent,
    double RemainingPercent,
    string StatusText,
    ProviderCardStatusTone StatusTone,
    double? DualBucketPrimaryUsed = null,
    double? DualBucketSecondaryUsed = null,
    bool IsStale = false)
{
    public bool HasDualBuckets => this.DualBucketPrimaryUsed.HasValue;
}
