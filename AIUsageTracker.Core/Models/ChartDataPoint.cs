namespace AIUsageTracker.Core.Models;

public class ChartDataPoint
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double RequestsPercentage { get; set; }
    public double RequestsUsed { get; set; }
}
