// <copyright file="PollingIntervalPolicy.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

public sealed class PollingIntervalPolicy : IPollingIntervalPolicy
{
    public TimeSpan DefaultInterval => TimeSpan.FromMinutes(1);

    public TimeSpan Normalize(TimeSpan requestedInterval)
    {
        if (requestedInterval <= TimeSpan.Zero)
        {
            return this.DefaultInterval;
        }

        if (requestedInterval < TimeSpan.FromSeconds(2))
        {
            return TimeSpan.FromSeconds(2);
        }

        if (requestedInterval > TimeSpan.FromMinutes(30))
        {
            return TimeSpan.FromMinutes(30);
        }

        return requestedInterval;
    }
}
