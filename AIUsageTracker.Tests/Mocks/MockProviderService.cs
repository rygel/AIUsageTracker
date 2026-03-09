// <copyright file="MockProviderService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Mocks
{
    using System;
    using System.Threading.Tasks;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Models;

    public class MockProviderService : IProviderService
    {
        private string _providerId = "mock-provider";
        public string ProviderId
        {
            get => this._providerId;
            set
            {
                this._providerId = value;
                this.Definition = new ProviderDefinition(
                    providerId: value,
                    displayName: value,
                    planType: PlanType.Usage,
                    isQuotaBased: false,
                    defaultConfigType: "pay-as-you-go");
            }
        }
    `n
        public ProviderDefinition Definition { get; set; } = new(
            providerId: "mock-provider",
            displayName: "mock-provider",
            planType: PlanType.Usage,
            isQuotaBased: false,
            defaultConfigType: "pay-as-you-go");

        public Func<ProviderConfig, Task<IEnumerable<ProviderUsage>>>? UsageHandler { get; set; }

        // Added for the new GetUsageAsync implementation
        private readonly Dictionary<string, ProviderUsage> _mockResponses = new();

        public MockProviderService() { }

        // Constructor to allow populating _mockResponses
        public MockProviderService(Dictionary<string, ProviderUsage> mockResponses)
        {
            this._mockResponses = mockResponses;
        }
    `n
        public Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
        {
            if (this.UsageHandler != null)
            {
                return this.UsageHandler(config);
            }

            if (this._mockResponses.TryGetValue(config.ProviderId, out var usage))
            {
                return Task.FromResult<IEnumerable<ProviderUsage>>(new[] { usage });
            }
            return Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage { ProviderId = config.ProviderId, IsAvailable = false, Description = "Mock not configured" } });
        }
    `n
        public static MockProviderService CreateOpenAIMock()
        {
            return new MockProviderService
            {
                ProviderId = "openai",
                UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
                {
                    ProviderId = "openai",
                    ProviderName = "OpenAI",
                    RequestsPercentage = 25,
                    RequestsUsed = 2.5,
                    RequestsAvailable = 10,
                    PlanType = PlanType.Usage,
                    UsageUnit = "USD",
                    Description = "$2.50 / $10.00 used",
                    IsAvailable = true
                }})
            };
        }
    `n
        public static MockProviderService CreateGeminiMock()
        {
            return new MockProviderService
            {
                ProviderId = "gemini",
                UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
                {
                    ProviderId = "gemini",
                    ProviderName = "Gemini",
                    RequestsPercentage = 10,
                    RequestsUsed = 150,
                    RequestsAvailable = 1500,
                    PlanType = PlanType.Coding,
                    UsageUnit = "Requests",
                    Description = "150 / 1500 requests",
                    IsAvailable = true
                }})
            };
        }
    `n
        public static MockProviderService CreateGeminiCliMock()
        {
            return new MockProviderService
            {
                ProviderId = "gemini-cli",
                UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
                {
                    ProviderId = "gemini-cli",
                    ProviderName = "Gemini CLI",
                    RequestsPercentage = 5,
                    RequestsUsed = 500,
                    RequestsAvailable = 10000,
                    PlanType = PlanType.Coding,
                    UsageUnit = "Tokens",
                    Description = "500 / 10,000 tokens",
                    IsAvailable = true
                }})
            };
        }
    `n
        public static MockProviderService CreateAntigravityMock()
        {
            return new MockProviderService
            {
                ProviderId = "antigravity",
                UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
                {
                    ProviderId = "antigravity",
                    ProviderName = "Antigravity",
                    RequestsPercentage = 40,
                    RequestsUsed = 4,
                    RequestsAvailable = 10,
                    PlanType = PlanType.Usage,
                    UsageUnit = "USD",
                    Description = "$6.00 remaining",
                    IsAvailable = true
                }})
            };
        }
    `n
        public static MockProviderService CreateOpenCodeZenMock()
        {
            return new MockProviderService
            {
                ProviderId = "opencode-zen",
                UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
                {
                    ProviderId = "opencode-zen",
                    ProviderName = "OpenCode Zen",
                    RequestsPercentage = 20,
                    RequestsUsed = 1,
                    RequestsAvailable = 5,
                    PlanType = PlanType.Coding,
                    UsageUnit = "Requests",
                    Description = "1 / 5 requests",
                    IsAvailable = true
                }})
            };
        }
    }

}
