using System.Text.Json.Serialization;

namespace AIConsumptionTracker.Core.Models;
 
public enum PaymentType
{
    UsageBased, // Postpaid (Spent X / Limit Y)
    Credits,    // Prepaid (Remaining X)
    Quota       // Recurring (Used X / Limit Y)
}

public class ProviderConfig
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "pay-as-you-go"; // "quota-based" or "pay-as-you-go"

    [JsonPropertyName("payment_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentType PaymentType { get; set; } = PaymentType.UsageBased;

    [JsonPropertyName("limit")]
    public double? Limit { get; set; } // For cost tracking

    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("show_in_tray")]
    public bool ShowInTray { get; set; }

    [JsonPropertyName("enabled_sub_trays")]
    public List<string> EnabledSubTrays { get; set; } = new();

    [JsonIgnore]
    public string AuthSource { get; set; } = "Unknown";

    [JsonIgnore]
    public string? Description { get; set; }
}

