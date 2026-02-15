using System.Text.Json.Serialization;

namespace AIConsumptionTracker.UI.Slim.Models;

public enum PaymentType
{
    [JsonPropertyName("usage_based")]
    UsageBased,
    [JsonPropertyName("credits")]
    Credits,
    [JsonPropertyName("quota")]
    Quota
}

public class ProviderUsage
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; set; } = string.Empty;
    
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = string.Empty;
    
    [JsonPropertyName("usage_percentage")]
    public double UsagePercentage { get; set; }
    
    [JsonPropertyName("cost_used")]
    public double CostUsed { get; set; }
    
    [JsonPropertyName("cost_limit")]
    public double CostLimit { get; set; }
    
    [JsonPropertyName("payment_type")]
    public PaymentType PaymentType { get; set; } = PaymentType.UsageBased;
    
    [JsonPropertyName("usage_unit")]
    public string UsageUnit { get; set; } = "USD";
    
    [JsonPropertyName("is_quota_based")]
    public bool IsQuotaBased { get; set; }
    
    [JsonPropertyName("is_available")]
    public bool IsAvailable { get; set; } = true;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("auth_source")]
    public string AuthSource { get; set; } = string.Empty;
    
    [JsonPropertyName("account_name")]
    public string AccountName { get; set; } = string.Empty;
    
    [JsonPropertyName("fetched_at")]
    public DateTime FetchedAt { get; set; }
    
    [JsonPropertyName("next_reset_time")]
    public DateTime? NextResetTime { get; set; }
    
    [JsonPropertyName("details")]
    public List<ProviderUsageDetail>? Details { get; set; }
}

public class ProviderUsageDetail
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("used")]
    public string Used { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("next_reset_time")]
    public DateTime? NextResetTime { get; set; }
}
