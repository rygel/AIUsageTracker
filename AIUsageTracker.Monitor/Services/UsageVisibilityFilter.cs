using AIUsageTracker.Core.Models;
using System.Text.Json;

namespace AIUsageTracker.Monitor.Services;

public static class UsageVisibilityFilter
{
    private static readonly HashSet<string> SystemProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "antigravity",
        "opencode-zen",
        "claude-code"
    };

    public static List<ProviderUsage> FilterForConfiguredProviders(
        IEnumerable<ProviderUsage> usages,
        IEnumerable<ProviderConfig> configs,
        bool? hasGeminiCliAccountsOverride = null)
    {
        var providersWithKeys = configs
            .Where(c => !string.IsNullOrWhiteSpace(c.ApiKey))
            .Select(c => c.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasGeminiCliAccounts = hasGeminiCliAccountsOverride ?? HasGeminiCliAccounts();

        return usages
            .Where(u => ShouldInclude(u, providersWithKeys, hasGeminiCliAccounts))
            .ToList();
    }

    private static bool ShouldInclude(
        ProviderUsage usage,
        HashSet<string> providersWithKeys,
        bool hasGeminiCliAccounts)
    {
        var providerId = usage.ProviderId;
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        if (providerId.Equals("gemini-cli", StringComparison.OrdinalIgnoreCase))
        {
            return hasGeminiCliAccounts && usage.IsAvailable;
        }

        if (providerId.StartsWith("antigravity.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((providerId.Equals("opencode", StringComparison.OrdinalIgnoreCase) ||
             providerId.Equals("opencode-zen", StringComparison.OrdinalIgnoreCase)) &&
            !usage.IsAvailable)
        {
            return false;
        }

        return SystemProviders.Contains(providerId) || providersWithKeys.Contains(providerId);
    }

    private static bool HasGeminiCliAccounts()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "opencode",
                "antigravity-accounts.json");

            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("accounts", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var account in accounts.EnumerateArray())
            {
                if (account.TryGetProperty("refreshToken", out var refreshToken) &&
                    !string.IsNullOrWhiteSpace(refreshToken.GetString()))
                {
                    return true;
                }
            }
        }
        catch
        {
            // If detection fails, treat Gemini CLI as unavailable to avoid stale false-positive display.
        }

        return false;
    }
}


