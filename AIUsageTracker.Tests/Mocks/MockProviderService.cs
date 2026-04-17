// <copyright file="MockProviderService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Tests.Mocks;

public class MockProviderService : IProviderService
{
    private readonly Dictionary<string, ProviderUsage> _mockResponses;
    private string _providerId = "mock-provider";

    public MockProviderService()
        : this(new Dictionary<string, ProviderUsage>(StringComparer.Ordinal))
    {
    }

    public MockProviderService(IReadOnlyDictionary<string, ProviderUsage> mockResponses)
    {
        this._mockResponses = new Dictionary<string, ProviderUsage>(mockResponses, StringComparer.Ordinal);
        this.Definition = CreateDefinition(this._providerId);
    }

    public string ProviderId
    {
        get => this._providerId;
        set
        {
            this._providerId = value;
            this.Definition = CreateDefinition(value);
        }
    }

    public ProviderDefinition Definition { get; private set; }

    public Func<ProviderConfig, Task<IEnumerable<ProviderUsage>>>? UsageHandler { get; set; }

    public static MockProviderService CreateOpenAIMock()
    {
        return CreateFixedUsageMock(
            providerId: "openai",
            providerName: "OpenAI",
            requestsPercentage: 25,
            requestsUsed: 2.5,
            requestsAvailable: 10,
            planType: PlanType.Usage,
            description: "$2.50 / $10.00 used");
    }

    public static MockProviderService CreateGeminiMock()
    {
        return CreateFixedUsageMock(
            providerId: "gemini",
            providerName: "Gemini",
            requestsPercentage: 10,
            requestsUsed: 150,
            requestsAvailable: 1500,
            planType: PlanType.Coding,
            description: "150 / 1500 requests");
    }

    public static MockProviderService CreateGeminiCliMock()
    {
        return CreateFixedUsageMock(
            providerId: "gemini-cli",
            providerName: "Gemini CLI",
            requestsPercentage: 5,
            requestsUsed: 500,
            requestsAvailable: 10000,
            planType: PlanType.Coding,
            description: "500 / 10,000 tokens");
    }

    public static MockProviderService CreateAntigravityMock()
    {
        return CreateFixedUsageMock(
            providerId: "antigravity",
            providerName: "Antigravity",
            requestsPercentage: 40,
            requestsUsed: 4,
            requestsAvailable: 10,
            planType: PlanType.Usage,
            description: "$6.00 remaining");
    }

    public static MockProviderService CreateOpenCodeZenMock()
    {
        return CreateFixedUsageMock(
            providerId: "opencode-zen",
            providerName: "OpenCode Zen",
            requestsPercentage: 20,
            requestsUsed: 1,
            requestsAvailable: 5,
            planType: PlanType.Coding,
            description: "1 / 5 requests");
    }

    public Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (this.UsageHandler != null)
        {
            return this.UsageHandler(config);
        }

        if (this._mockResponses.TryGetValue(config.ProviderId, out var usage))
        {
            return Task.FromResult<IEnumerable<ProviderUsage>>(new[] { usage });
        }

        return Task.FromResult<IEnumerable<ProviderUsage>>(
            new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    IsAvailable = false,
                    Description = "Mock not configured",
                },
            });
    }

    private static ProviderDefinition CreateDefinition(string providerId)
    {
        return new ProviderDefinition(
            providerId: providerId,
            displayName: providerId,
            planType: PlanType.Usage,
            isQuotaBased: false);
    }

    private static MockProviderService CreateFixedUsageMock(
        string providerId,
        string providerName,
        double requestsPercentage,
        double requestsUsed,
        double requestsAvailable,
        PlanType planType,
        string description)
    {
        return new MockProviderService
        {
            ProviderId = providerId,
            UsageHandler = _ => Task.FromResult<IEnumerable<ProviderUsage>>(
                new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = providerId,
                        ProviderName = providerName,
                        UsedPercent = requestsPercentage,
                        RequestsUsed = requestsUsed,
                        RequestsAvailable = requestsAvailable,
                        PlanType = planType,
                        Description = description,
                        IsAvailable = true,
                    },
                }),
        };
    }
}
