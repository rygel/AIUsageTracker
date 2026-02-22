using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public static class ProviderMetadataService
{
    // Static mapping of provider IDs to their metadata
    // This avoids storing redundant data in the database
    private static readonly Dictionary<string, ProviderMetadata> _metadata = new(StringComparer.OrdinalIgnoreCase)
    {
        // Quota-based providers (Coding plans)
        ["zai-coding-plan"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["antigravity"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["github-copilot"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["kimi"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["gemini-cli"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["xiaomi"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["minimax"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["codex"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["evolve-migrations"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["simulated"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        ["synthetic"] = new ProviderMetadata { PlanType = PlanType.Coding, IsQuotaBased = true },
        
        // Pay-as-you-go providers (Usage plans)
        ["openai"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["anthropic"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["deepseek"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["openrouter"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["mistral"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["opencode"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["opencode-zen"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["claude-code"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["cloud-code"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
        ["generic-pay-as-you-go"] = new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false },
    };

    public static ProviderMetadata GetMetadata(string providerId)
    {
        if (_metadata.TryGetValue(providerId, out var metadata))
            return metadata;
        
        // Default to usage plan if unknown
        return new ProviderMetadata { PlanType = PlanType.Usage, IsQuotaBased = false };
    }

    public static void EnrichUsageData(ProviderUsage usage)
    {
        var metadata = GetMetadata(usage.ProviderId);
        usage.PlanType = metadata.PlanType;
        usage.IsQuotaBased = metadata.IsQuotaBased;
    }
}

public class ProviderMetadata
{
    public PlanType PlanType { get; set; }
    public bool IsQuotaBased { get; set; }
}


