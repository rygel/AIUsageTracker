using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;
 
public enum PlanType
{
    Usage,
    Coding
}

public class ProviderConfig
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "pay-as-you-go"; // "quota-based" or "pay-as-you-go"

    [JsonPropertyName("plan_type")]
    [JsonConverter(typeof(JsonStringEnumConverter<PlanType>))]
    public PlanType PlanType { get; set; } = PlanType.Usage;

    [JsonPropertyName("limit")]
    public double? Limit { get; set; } // For cost tracking

    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("show_in_tray")]
    public bool ShowInTray { get; set; }

    [JsonPropertyName("enable_notifications")]
    public bool EnableNotifications { get; set; } = false; // Default to disabled

    [JsonPropertyName("enabled_sub_trays")]
    public List<string> EnabledSubTrays { get; set; } = new();

    [JsonIgnore]
    public string AuthSource { get; set; } = "Unknown";

    [JsonIgnore]
    public string? Description { get; set; }

    [JsonPropertyName("models")]
    public List<AIModelConfig> Models { get; set; } = new();
}


