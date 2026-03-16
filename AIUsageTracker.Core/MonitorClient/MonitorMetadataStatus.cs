// <copyright file="MonitorMetadataStatus.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class MonitorMetadataStatus
{
    public bool IsUsable { get; init; }

    public MonitorInfo? Info { get; init; }
}
