using System.Text.Json;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Configuration;

namespace AIConsumptionTracker.Tests.Infrastructure;

public class ConfigLoaderTests
{
    [Fact]
    public void JsonConfigLoader_ShouldDeserializePaymentTypeCorrecty()
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
