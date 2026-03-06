using System.Windows.Media;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderVisualDefinition(
    string IconAssetName,
    Brush? BadgeColor = null,
    string? BadgeInitial = null);

internal static class ProviderVisualCatalog
{
    private static readonly IReadOnlyDictionary<string, ProviderVisualDefinition> VisualDefinitions =
        new Dictionary<string, ProviderVisualDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["github-copilot"] = new("github", Brushes.MediumPurple, "GH"),
            ["gemini-cli"] = new("google", Brushes.DodgerBlue, "G"),
            ["gemini"] = new("google", Brushes.DodgerBlue, "G"),
            ["antigravity"] = new("google", Brushes.DodgerBlue, "G"),
            ["claude-code"] = new("anthropic", Brushes.Orange, "C"),
            ["claude"] = new("anthropic", Brushes.Orange, "C"),
            ["anthropic"] = new("anthropic", Brushes.IndianRed, "An"),
            ["minimax"] = new("minimax", Brushes.DarkTurquoise, "MM"),
            ["kimi"] = new("kimi", Brushes.MediumOrchid, "K"),
            ["xiaomi"] = new("xiaomi", Brushes.Orange, "Xi"),
            ["zai"] = new("zai", Brushes.LightSeaGreen, "Z"),
            ["zai-coding-plan"] = new("zai", Brushes.LightSeaGreen, "Z"),
            ["deepseek"] = new("deepseek", Brushes.DeepSkyBlue, "DS"),
            ["openrouter"] = new("openai", Brushes.DarkSlateBlue, "OR"),
            ["codex"] = new("openai", Brushes.DarkCyan, "AI"),
            ["openai"] = new("openai", Brushes.DarkCyan, "AI"),
            ["mistral"] = new("mistral", Brushes.OrangeRed, "Mi"),
            ["github"] = new("github"),
            ["google"] = new("google", Brushes.DodgerBlue, "G"),
            ["cloudcode"] = new("cloudcode", Brushes.DeepSkyBlue, "CC"),
            ["synthetic"] = new("synthetic", Brushes.Gold, "Sy")
        };

    public static string GetCanonicalProviderId(string providerId)
    {
        return ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
    }

    public static string GetIconAssetName(string providerId)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        return TryGetVisualDefinition(canonicalProviderId, out var definition)
            ? definition.IconAssetName
            : canonicalProviderId;
    }

    public static (Brush Color, string Initial) GetFallbackBadge(string providerId, Brush defaultBrush)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        return TryGetVisualDefinition(canonicalProviderId, out var definition) &&
               definition.BadgeColor != null &&
               !string.IsNullOrWhiteSpace(definition.BadgeInitial)
            ? (definition.BadgeColor, definition.BadgeInitial)
            : (defaultBrush, canonicalProviderId[..Math.Min(2, canonicalProviderId.Length)].ToUpperInvariant());
    }

    private static bool TryGetVisualDefinition(string providerId, out ProviderVisualDefinition definition)
    {
        return VisualDefinitions.TryGetValue(providerId, out definition!);
    }
}
