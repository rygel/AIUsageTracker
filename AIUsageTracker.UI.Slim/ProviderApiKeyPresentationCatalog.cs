namespace AIUsageTracker.UI.Slim;

internal static class ProviderApiKeyPresentationCatalog
{
    public static string GetDisplayApiKey(string? apiKey, bool isPrivacyMode)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return apiKey ?? string.Empty;
        }

        if (!isPrivacyMode)
        {
            return apiKey;
        }

        if (apiKey.Length > 8)
        {
            return apiKey[..4] + "****" + apiKey[^4..];
        }

        return "****";
    }
}
