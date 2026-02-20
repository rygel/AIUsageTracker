namespace AIConsumptionTracker.Core.Models;

public static class ProviderPlanClassifier
{
    private static readonly HashSet<string> CodingPlanProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "antigravity",
        "synthetic",
        "zai-coding-plan",
        "github-copilot",
        "gemini-cli",
        "kimi"
    };

    public static bool IsCodingPlanProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        if (providerId.StartsWith("antigravity.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CodingPlanProviders.Contains(providerId);
    }
}
