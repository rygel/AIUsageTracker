// <copyright file="AnthropicProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;

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

    public override Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return Task.FromResult<IEnumerable<ProviderUsage>>(new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = "Anthropic",
                    IsAvailable = false,
                    Description = "API Key missing",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    AuthSource = config.AuthSource,
                    RawJson = "{\"source\":\"anthropic\",\"status\":\"api_key_missing\"}",
                    HttpStatus = 401,
                },
            });
        }

        return Task.FromResult<IEnumerable<ProviderUsage>>(new[]
        {
            new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = "Anthropic",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                Description = "Connected (Check Dashboard)",
                AuthSource = config.AuthSource,
                RawJson = "{\"source\":\"anthropic\",\"status\":\"connected_no_usage_endpoint\"}",
                HttpStatus = 200,
            },
        });
    }
}
