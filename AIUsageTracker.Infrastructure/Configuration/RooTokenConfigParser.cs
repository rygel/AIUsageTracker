using System.Text.Json;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Infrastructure.Configuration;

internal static class RooTokenConfigParser
{
    public static IReadOnlyList<DiscoveredProviderToken> Parse(string rooJson)
    {
        using var document = JsonDocument.Parse(rooJson);
        return Parse(document.RootElement);
    }

    public static IReadOnlyList<DiscoveredProviderToken> Parse(JsonElement root)
    {
        var tokens = new List<DiscoveredProviderToken>();
        if (!root.TryGetProperty("apiConfigs", out var configsProperty) || configsProperty.ValueKind != JsonValueKind.Object)
        {
            return tokens;
        }

        foreach (var configEntry in configsProperty.EnumerateObject())
        {
            AddProviderTokens(tokens, configEntry.Value);
        }

        return tokens;
    }

    private static void AddProviderTokens(List<DiscoveredProviderToken> tokens, JsonElement config)
    {
        foreach (var definition in ProviderMetadataCatalog.Definitions.Where(d => d.RooConfigPropertyNames.Count > 0))
        {
            foreach (var propertyName in definition.RooConfigPropertyNames)
            {
                if (!config.TryGetProperty(propertyName, out var keyProperty))
                {
                    continue;
                }

                var apiKey = keyProperty.GetString();
                if (string.IsNullOrEmpty(apiKey))
                {
                    continue;
                }

                tokens.Add(new DiscoveredProviderToken(definition.ProviderId, apiKey));
            }
        }
    }

    internal readonly record struct DiscoveredProviderToken(string ProviderId, string ApiKey);
}
