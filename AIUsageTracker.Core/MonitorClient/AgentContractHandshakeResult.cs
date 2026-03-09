// <copyright file="AgentContractHandshakeResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient
{
    public sealed class AgentContractHandshakeResult
    {
        public bool IsReachable { get; init; }

        public bool IsCompatible { get; init; }

        public string? AgentContractVersion { get; init; }

        public string? AgentVersion { get; init; }

        public string Message { get; init; } = string.Empty;
    }
}
