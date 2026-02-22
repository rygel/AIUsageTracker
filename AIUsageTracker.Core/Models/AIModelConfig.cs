using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;

public class AIModelConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("matches")]
    public List<string> Matches { get; set; } = new();

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

