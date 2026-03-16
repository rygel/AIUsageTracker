// <copyright file="RooTokenConfigParser.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
        foreach (var property in config.EnumerateObject())
        {
            var definition = ProviderMetadataCatalog.FindByRooConfigProperty(property.Name);
            if (definition == null)
            {
                continue;
            }

            var apiKey = property.Value.GetString();
            if (string.IsNullOrEmpty(apiKey))
            {
                continue;
            }

            tokens.Add(new DiscoveredProviderToken(definition.ProviderId, apiKey));
        }
    }

    internal readonly record struct DiscoveredProviderToken(string ProviderId, string ApiKey);
}
