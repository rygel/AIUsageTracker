// <copyright file="AgentTestNotificationResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient
{
    public sealed class AgentTestNotificationResult
    {
        public bool Success { get; init; }

        public string Message { get; init; } = string.Empty;
    }
}
