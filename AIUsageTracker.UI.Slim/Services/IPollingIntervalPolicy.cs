// <copyright file="IPollingIntervalPolicy.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

public interface IPollingIntervalPolicy
{
    TimeSpan DefaultInterval { get; }

    TimeSpan Normalize(TimeSpan requestedInterval);
}
