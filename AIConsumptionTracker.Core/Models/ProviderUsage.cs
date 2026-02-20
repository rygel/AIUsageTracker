namespace AIConsumptionTracker.Core.Models;

public class ProviderUsage
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public double RequestsUsed { get; set; }
    public double RequestsAvailable { get; set; }
    public double RequestsPercentage { get; set; }
    public PlanType PlanType { get; set; } = PlanType.Usage;

    public string UsageUnit { get; set; } = "USD";
    public bool IsQuotaBased { get; set; }
    public bool DisplayAsFraction { get; set; } // Explicitly request "X / Y" display format
    public bool IsAvailable { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string AuthSource { get; set; } = string.Empty;
    public List<ProviderUsageDetail>? Details { get; set; }
    
    // Temporary property for database serialization - not serialized to JSON
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DetailsJson { get; set; }
    
    public string AccountName { get; set; } = string.Empty;
    public string ConfigKey { get; set; } = string.Empty;
    public DateTime? NextResetTime { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public string? RawJson { get; set; }
    public int HttpStatus { get; set; } = 200;
}

public class ProviderUsageDetail
{
    public string Name { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string Used { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? NextResetTime { get; set; }
}
