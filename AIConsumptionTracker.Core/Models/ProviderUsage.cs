namespace AIConsumptionTracker.Core.Models;

public class ProviderUsage
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public double UsagePercentage { get; set; }
    public double CostUsed { get; set; }
    public double CostLimit { get; set; }
    public PaymentType PaymentType { get; set; } = PaymentType.UsageBased;

    public string UsageUnit { get; set; } = "USD"; // USD, Tokens, etc.
    public bool IsQuotaBased { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string Description { get; set; } = string.Empty; // e.g., "23/100 remaining"
    public string AuthSource { get; set; } = string.Empty;
    public List<ProviderUsageDetail>? Details { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateTime? NextResetTime { get; set; }
}

public class ProviderUsageDetail
{
    public string Name { get; set; } = string.Empty;
    public string Used { get; set; } = string.Empty; // Pre-formatted string for the Used column
    public string Description { get; set; } = string.Empty;
    public DateTime? NextResetTime { get; set; }
}

