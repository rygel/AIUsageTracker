namespace AIUsageTracker.Core.Models;

public class AgentInfo
{
    public int Port { get; set; }
    public string? StartedAt { get; set; }
    public int ProcessId { get; set; }
    public bool DebugMode { get; set; }
    public List<string>? Errors { get; set; }
    public string? MachineName { get; set; }
    public string? UserName { get; set; }
}

