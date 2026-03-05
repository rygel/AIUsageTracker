namespace AIUsageTracker.Core.Models;

public class UsageSummary
{
    public int ProviderCount { get; set; }
    public double AverageUsage { get; set; }
    public string? LastUpdate { get; set; }
}
