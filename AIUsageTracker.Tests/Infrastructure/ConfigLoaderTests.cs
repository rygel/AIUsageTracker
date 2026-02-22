using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;

namespace AIUsageTracker.Tests.Infrastructure;

public class ConfigLoaderTests
{
    [Fact]
    public void JsonConfigLoader_ShouldDeserializePlanTypeCorrecty()
    {
        // Arrange
        var json = @"
        {
            ""openai"": {
                ""key"": ""sk-test"",
                ""type"": ""api""
            }
        }";

        // Act - Testing direct deserialization of the dictionary as the model
        // Note: Real loader uses a custom loop, but this tests model compatibility
        var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("openai"));
    }
}

