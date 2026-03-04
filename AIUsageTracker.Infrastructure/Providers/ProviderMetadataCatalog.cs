using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

public static class ProviderMetadataCatalog
{
    private static readonly IReadOnlyList<ProviderDefinition> DefinitionList = new List<ProviderDefinition>
    {
        AntigravityProvider.StaticDefinition,
        AnthropicProvider.StaticDefinition,
        ClaudeCodeProvider.StaticDefinition,
        CodexProvider.StaticDefinition,
        DeepSeekProvider.StaticDefinition,
        GeminiProvider.StaticDefinition,
        GitHubCopilotProvider.StaticDefinition,
        KimiProvider.StaticDefinition,
        MinimaxProvider.StaticDefinition,
        MistralProvider.StaticDefinition,
        OpenAIProvider.StaticDefinition,
        OpenCodeProvider.StaticDefinition,
        OpenCodeZenProvider.StaticDefinition,
        OpenRouterProvider.StaticDefinition,
        SyntheticProvider.StaticDefinition,
        XiaomiProvider.StaticDefinition,
        ZaiProvider.StaticDefinition
    };

    static ProviderMetadataCatalog()
    {
        var duplicateProviderIds = DefinitionList
            .GroupBy(d => d.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateProviderIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate provider definitions detected: {string.Join(", ", duplicateProviderIds)}");
        }
    }

    public static IReadOnlyList<ProviderDefinition> Definitions => DefinitionList;

    public static ProviderDefinition? Find(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return Definitions.FirstOrDefault(definition => definition.HandlesProviderId(providerId));
    }

    public static bool TryGet(string providerId, out ProviderDefinition definition)
    {
        var found = Find(providerId);
        if (found == null)
        {
            definition = null!;
            return false;
        }

        definition = found;
        return true;
    }

    public static string GetDisplayName(string providerId, string? providerName = null)
    {
        if (TryGet(providerId, out var definition))
        {
            var mapped = definition.ResolveDisplayName(providerId);
            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return providerName;
        }

        return providerId ?? string.Empty;
    }
}
