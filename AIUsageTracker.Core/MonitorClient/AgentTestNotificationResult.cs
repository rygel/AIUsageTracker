namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentTestNotificationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
