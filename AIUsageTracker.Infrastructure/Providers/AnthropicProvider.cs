using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class AnthropicProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "anthropic",
        displayName: "Anthropic",
        planType: PlanType.Usage,
        isQuotaBased: false,
        defaultConfigType: "pay-as-you-go");

    public override ProviderDefinition Definition => StaticDefinition;
    public override string ProviderId => StaticDefinition.ProviderId;
    private readonly ILogger<AnthropicProvider> _logger;

    public AnthropicProvider(ILogger<AnthropicProvider> logger)
    {
        _logger = logger;
    }

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Anthropic",
                    IsAvailable = false,
                    Description = "API Key missing",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    AuthSource = config.AuthSource,
                    RawJson = "{\"source\":\"anthropic\",\"status\":\"api_key_missing\"}",
                    HttpStatus = 401
                }
            };
        }

        return new[]
        {
            new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Anthropic",
                IsAvailable = true,
                RequestsPercentage = 0.0,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                Description = "Connected (Check Dashboard)",
                UsageUnit = "Status",
                AuthSource = config.AuthSource,
                RawJson = "{\"source\":\"anthropic\",\"status\":\"connected_no_usage_endpoint\"}",
                HttpStatus = 200
            }
        };
    }
}


