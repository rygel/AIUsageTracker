using System;
using System.Threading.Tasks;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Tests.Mocks;

public class MockProviderService : IProviderService
{
    public string ProviderId { get; set; } = "mock-provider";
    public Func<ProviderConfig, Task<ProviderUsage>>? UsageHandler { get; set; }

    public Task<ProviderUsage> GetUsageAsync(ProviderConfig config)
    {
        if (UsageHandler != null)
        {
            return UsageHandler(config);
        }

        return Task.FromResult(new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Mock Provider",
            UsagePercentage = 50,
            Description = "Mock Usage",
            IsAvailable = true
        });
    }

    public static MockProviderService CreateOpenAIMock()
    {
        return new MockProviderService
        {
            ProviderId = "openai",
            UsageHandler = config => Task.FromResult(new ProviderUsage
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                UsagePercentage = 25,
                CostUsed = 2.5,
                CostLimit = 10,
                PaymentType = PaymentType.UsageBased,
                UsageUnit = "USD",
                Description = "$2.50 / $10.00 used",
                IsAvailable = true
            })
        };
    }

    public static MockProviderService CreateAnthropicMock()
    {
        return new MockProviderService
        {
            ProviderId = "anthropic",
            UsageHandler = config => Task.FromResult(new ProviderUsage
            {
                ProviderId = "anthropic",
                ProviderName = "Anthropic",
                UsagePercentage = 75,
                CostUsed = 15,
                CostLimit = 20,
                PaymentType = PaymentType.Credits,
                UsageUnit = "USD",
                Description = "$5.00 remaining",
                IsAvailable = true
            })
        };
    }

    public static MockProviderService CreateGeminiMock()
    {
        return new MockProviderService
        {
            ProviderId = "gemini",
            UsageHandler = config => Task.FromResult(new ProviderUsage
            {
                ProviderId = "gemini",
                ProviderName = "Gemini",
                UsagePercentage = 10,
                CostUsed = 150,
                CostLimit = 1500,
                PaymentType = PaymentType.Quota,
                UsageUnit = "Requests",
                Description = "150 / 1500 requests",
                IsAvailable = true
            })
        };
    }

    public static MockProviderService CreateGeminiCliMock()
    {
        return new MockProviderService
        {
            ProviderId = "gemini-cli",
            UsageHandler = config => Task.FromResult(new ProviderUsage
            {
                ProviderId = "gemini-cli",
                ProviderName = "Gemini CLI",
                UsagePercentage = 5,
                CostUsed = 500,
                CostLimit = 10000,
                PaymentType = PaymentType.Quota,
                UsageUnit = "Tokens",
                Description = "500 / 10,000 tokens",
                IsAvailable = true
            })
        };
    }

    public static MockProviderService CreateAntigravityMock()
    {
        return new MockProviderService
        {
            ProviderId = "antigravity",
            UsageHandler = config => Task.FromResult(new ProviderUsage
            {
                ProviderId = "antigravity",
                ProviderName = "Antigravity",
                UsagePercentage = 40,
                CostUsed = 4,
                CostLimit = 10,
                PaymentType = PaymentType.Credits,
                UsageUnit = "USD",
                Description = "$6.00 remaining",
                IsAvailable = true
            })
        };
    }

    public static MockProviderService CreateOpenCodeZenMock()
    {
        return new MockProviderService
        {
            ProviderId = "opencode-zen",
            UsageHandler = config => Task.FromResult(new ProviderUsage
            {
                ProviderId = "opencode-zen",
                ProviderName = "OpenCode Zen",
                UsagePercentage = 20,
                CostUsed = 1,
                CostLimit = 5,
                PaymentType = PaymentType.Quota,
                UsageUnit = "Requests",
                Description = "1 / 5 requests",
                IsAvailable = true
            })
        };
    }
}
