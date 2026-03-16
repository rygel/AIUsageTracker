// <copyright file="AgentScanKeysResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentScanKeysResult
{
    public int Count { get; init; }

    public IReadOnlyList<ProviderConfig> Configs { get; init; } = [];
}
