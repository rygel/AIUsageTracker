using System.Text.Json.Serialization;

namespace AIConsumptionTracker.UI.Slim.Models;

public class ProviderConfig
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "pay-as-you-go";

    [JsonPropertyName("payment_type")]
    public PaymentType PaymentType { get; set; } = PaymentType.UsageBased;

    [JsonPropertyName("limit")]
    public double? Limit { get; set; }

    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("show_in_tray")]
    public bool ShowInTray { get; set; }

    [JsonPropertyName("enable_notifications")]
    public bool EnableNotifications { get; set; } = false;

    [JsonPropertyName("enabled_sub_trays")]
    public List<string> EnabledSubTrays { get; set; } = new();

    [JsonPropertyName("auth_source")]
    public string AuthSource { get; set; } = "Unknown";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
