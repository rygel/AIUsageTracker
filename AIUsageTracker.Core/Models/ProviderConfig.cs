using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;

public class ProviderConfig
{
    [Required(ErrorMessage = "ProviderId is required")]
    [StringLength(100, MinimumLength = 1)]
    [RegularExpression(@"^[a-z0-9\-]+$", ErrorMessage = "ProviderId must contain only lowercase letters, numbers, and hyphens")]
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; set; } = string.Empty;

    [StringLength(500, MinimumLength = 0)]
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [RegularExpression(@"^(quota\-based|pay\-as\-you\-go)$", ErrorMessage = "Type must be 'quota-based' or 'pay-as-you-go'")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = "pay-as-you-go"; // "quota-based" or "pay-as-you-go"

    [JsonPropertyName("plan_type")]
    [JsonConverter(typeof(JsonStringEnumConverter<PlanType>))]
    public PlanType PlanType { get; set; } = PlanType.Usage;

    [Range(0, double.MaxValue, ErrorMessage = "Limit must be non-negative")]
    [JsonPropertyName("limit")]
    public double? Limit { get; set; } // For cost tracking

    [StringLength(500)]
    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("show_in_tray")]
    public bool ShowInTray { get; set; }

    [JsonPropertyName("enable_notifications")]
    public bool EnableNotifications { get; set; } = false; // Default to disabled

    [JsonPropertyName("enabled_sub_trays")]
    public IReadOnlyList<string> EnabledSubTrays { get; set; } = [];

    [JsonIgnore]
    [StringLength(100)]
    public string AuthSource { get; set; } = string.Empty;

    [JsonIgnore]
    public string? Description { get; set; }

    [JsonPropertyName("models")]
    public IReadOnlyList<AIModelConfig> Models { get; set; } = [];
}
