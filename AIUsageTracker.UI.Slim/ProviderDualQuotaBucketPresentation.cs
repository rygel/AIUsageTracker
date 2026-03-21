// <copyright file="ProviderDualQuotaBucketPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderDualQuotaBucketPresentation(
    string PrimaryLabel,
    double PrimaryUsedPercent,
    DateTime? PrimaryResetTime,
    TimeSpan? PrimaryPeriodDuration,
    string SecondaryLabel,
    double SecondaryUsedPercent,
    DateTime? SecondaryResetTime,
    TimeSpan? SecondaryPeriodDuration);
