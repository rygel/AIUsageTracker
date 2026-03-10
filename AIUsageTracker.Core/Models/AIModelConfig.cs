// <copyright file="AIModelConfig.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;

public class AIModelConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("matches")]
    public IReadOnlyList<string> Matches { get; set; } = [];

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}
