using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal enum ProviderInputMode
{
    StandardApiKey,
    DerivedReadOnly,
    AntigravityAutoDetected,
    GitHubCopilotAuthStatus,
    OpenAiSessionStatus
}

internal static class ProviderSettingsCatalog
{
    public static ProviderInputMode GetInputMode(ProviderConfig config, ProviderUsage? usage, bool isDerived)
    {
        if (isDerived)
        {
            return ProviderInputMode.DerivedReadOnly;
        }

        return ProviderMetadataCatalog.GetCanonicalProviderId(config.ProviderId) switch
        {
            "antigravity" => ProviderInputMode.AntigravityAutoDetected,
            "github-copilot" => ProviderInputMode.GitHubCopilotAuthStatus,
            "codex" => ProviderInputMode.OpenAiSessionStatus,
            "openai" when usage?.IsQuotaBased == true || IsSessionToken(config.ApiKey) => ProviderInputMode.OpenAiSessionStatus,
            _ => ProviderInputMode.StandardApiKey
        };
    }

    public static bool IsInactive(ProviderConfig config, ProviderUsage? usage, bool isDerived, ProviderInputMode inputMode)
    {
        if (isDerived)
        {
            return false;
        }

        return inputMode switch
        {
            ProviderInputMode.AntigravityAutoDetected => usage == null || !usage.IsAvailable,
            ProviderInputMode.OpenAiSessionStatus => string.IsNullOrWhiteSpace(config.ApiKey) && !(usage?.IsAvailable == true),
            _ => string.IsNullOrWhiteSpace(config.ApiKey)
        };
    }

    public static bool IsDerivedProviderVisible(string? providerId)
    {
        return string.Equals(providerId, "codex.spark", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSessionToken(string? apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) &&
               !apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }
}
