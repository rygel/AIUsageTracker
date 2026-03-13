// <copyright file="UsageUpdatedEventArgs.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Event arguments for usage data updates.
/// </summary>
public class UsageUpdatedEventArgs : EventArgs
{
    public UsageUpdatedEventArgs(IReadOnlyList<ProviderUsage> usages)
    {
        this.Usages = usages;
    }

    public IReadOnlyList<ProviderUsage> Usages { get; }
}
