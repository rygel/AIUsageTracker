using System.Windows.Media;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderVisualCatalog
{
    public static string GetCanonicalProviderId(string providerId)
    {
        return ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
    }

    public static string GetIconAssetName(string providerId)
    {
        return GetCanonicalProviderId(providerId).ToLowerInvariant() switch
        {
            "github-copilot" => "github",
            "gemini-cli" or "gemini" or "antigravity" => "google",
            "claude-code" or "claude" => "anthropic",
            "minimax" => "minimax",
            "kimi" => "kimi",
            "xiaomi" => "xiaomi",
            "zai" or "zai-coding-plan" => "zai",
            "deepseek" => "deepseek",
            "openrouter" or "codex" or "openai" => "openai",
            "mistral" => "mistral",
            "github" => "github",
            "google" => "google",
            var canonical => canonical
        };
    }

    public static (Brush Color, string Initial) GetFallbackBadge(string providerId, Brush defaultBrush)
    {
        return GetCanonicalProviderId(providerId).ToLowerInvariant() switch
        {
            "openai" or "codex" => (Brushes.DarkCyan, "AI"),
            "anthropic" => (Brushes.IndianRed, "An"),
            "github-copilot" => (Brushes.MediumPurple, "GH"),
            "gemini" or "gemini-cli" or "google" or "antigravity" => (Brushes.DodgerBlue, "G"),
            "deepseek" => (Brushes.DeepSkyBlue, "DS"),
            "openrouter" => (Brushes.DarkSlateBlue, "OR"),
            "kimi" => (Brushes.MediumOrchid, "K"),
            "minimax" => (Brushes.DarkTurquoise, "MM"),
            "mistral" => (Brushes.OrangeRed, "Mi"),
            "xiaomi" => (Brushes.Orange, "Xi"),
            "zai" or "zai-coding-plan" => (Brushes.LightSeaGreen, "Z"),
            "claude-code" or "claude" => (Brushes.Orange, "C"),
            "cloudcode" => (Brushes.DeepSkyBlue, "CC"),
            "synthetic" => (Brushes.Gold, "Sy"),
            var canonical => (defaultBrush, canonical[..Math.Min(2, canonical.Length)].ToUpperInvariant())
        };
    }
}
