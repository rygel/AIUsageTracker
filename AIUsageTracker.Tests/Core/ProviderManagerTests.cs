// <copyright file="ProviderManagerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Core;

public class ProviderManagerTests
{
    private readonly Mock<ILogger<ProviderManager>> _mockLogger;
    private readonly Mock<IConfigLoader> _mockConfigLoader;

    public ProviderManagerTests()
    {
        this._mockLogger = new Mock<ILogger<ProviderManager>>();
        this._mockConfigLoader = new Mock<IConfigLoader>();
    }

    [Fact]
    public async Task GetAllUsageAsync_LoadsConfigAndFetchesUsageFromMocksAsync()
    {
        var providers = new List<IProviderService>
        {
            MockProviderService.CreateOpenAIMock(),
            MockProviderService.CreateGeminiMock(),
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "gemini" },
        };

        this._mockConfigLoader.Setup(configLoader => configLoader.LoadConfigAsync()).ReturnsAsync(configs);

        var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        var result = await manager.GetAllUsageAsync();

        Assert.True(result.Count(usage => usage.IsAvailable) >= 2);
        Assert.Contains(
            result,
            usage => usage.PlanType == PlanType.Usage &&
                string.Equals(usage.ProviderId, "openai", StringComparison.Ordinal));
        Assert.Contains(
            result,
            usage => usage.PlanType == PlanType.Coding &&
                string.Equals(usage.ProviderId, "gemini", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAllUsageAsync_WhenProviderIntegrationMissing_ReturnsUnavailableUsageAsync()
    {
        var providers = new List<IProviderService>();
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "unknown-api", Type = "api" },
        };

        this._mockConfigLoader.Setup(configLoader => configLoader.LoadConfigAsync()).ReturnsAsync(configs);
        var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        var result = await manager.GetAllUsageAsync();

        Assert.Contains(
            result,
            usage =>
                string.Equals(usage.ProviderId, "unknown-api", StringComparison.Ordinal) &&
                !usage.IsAvailable &&
                string.Equals(usage.Description, "Usage unknown (provider integration missing)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAllUsageAsync_WhenIncludeProviderIdsProvided_FetchesOnlyIncludedProvidersAsync()
    {
        var providers = new List<IProviderService>
        {
            MockProviderService.CreateOpenAIMock(),
            MockProviderService.CreateGeminiMock(),
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "gemini" },
        };

        this._mockConfigLoader.Setup(configLoader => configLoader.LoadConfigAsync()).ReturnsAsync(configs);
        var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        var result = await manager.GetAllUsageAsync(includeProviderIds: new[] { "openai" });

        Assert.Contains(result, usage => string.Equals(usage.ProviderId, "openai", StringComparison.Ordinal));
        Assert.DoesNotContain(result, usage => string.Equals(usage.ProviderId, "gemini", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-5, ProviderManager.MinMaxConcurrentProviderRequests)]
    [InlineData(0, ProviderManager.MinMaxConcurrentProviderRequests)]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(99, ProviderManager.MaxMaxConcurrentProviderRequests)]
    public void ClampMaxConcurrentProviderRequests_ClampsWithinExpectedRange(int configured, int expected)
    {
        var clamped = ProviderManager.ClampMaxConcurrentProviderRequests(configured);
        Assert.Equal(expected, clamped);
    }

    [Fact]
    public void Constructor_UsesClampedMaxConcurrentProviderRequests()
    {
        var managerLow = new ProviderManager(
            providers: [],
            configLoader: this._mockConfigLoader.Object,
            logger: this._mockLogger.Object,
            maxConcurrentProviderRequests: -3);
        Assert.Equal(ProviderManager.MinMaxConcurrentProviderRequests, managerLow.MaxConcurrentProviderRequests);

        var managerHigh = new ProviderManager(
            providers: [],
            configLoader: this._mockConfigLoader.Object,
            logger: this._mockLogger.Object,
            maxConcurrentProviderRequests: 300);
        Assert.Equal(ProviderManager.MaxMaxConcurrentProviderRequests, managerHigh.MaxConcurrentProviderRequests);
    }

    [Fact]
    public async Task GetUsageAsync_WhenAutoIncludedProviderMissingConfig_UsesGeneratedDefaultConfigAsync()
    {
        var configLoader = new Mock<IConfigLoader>();
        configLoader.Setup(loader => loader.LoadConfigAsync()).ReturnsAsync([]);

        ProviderConfig? capturedConfig = null;
        var provider = new Mock<IProviderService>();
        provider.SetupGet(p => p.Definition).Returns(new ProviderDefinition(
            providerId: "github-copilot",
            displayName: "GitHub Copilot",
            planType: PlanType.Coding,
            isQuotaBased: true,
            defaultConfigType: "quota-based",
            autoIncludeWhenUnconfigured: true));
        provider.SetupGet(p => p.ProviderId).Returns("github-copilot");
        provider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .Callback<ProviderConfig, Action<ProviderUsage>?>((config, _) => capturedConfig = config)
            .ReturnsAsync(new[]
            {
                new ProviderUsage
                {
                    ProviderId = "github-copilot",
                    ProviderName = "GitHub Copilot",
                    IsAvailable = true,
                    PlanType = PlanType.Coding,
                },
            });

        var manager = new ProviderManager(
            new[] { provider.Object },
            configLoader.Object,
            this._mockLogger.Object);

        var result = await manager.GetUsageAsync("github-copilot");

        var usage = Assert.Single(result);
        Assert.True(usage.IsAvailable);
        Assert.NotNull(capturedConfig);
        Assert.Equal("github-copilot", capturedConfig!.ProviderId);
        Assert.Equal("quota-based", capturedConfig.Type);
        Assert.Equal(PlanType.Coding, capturedConfig.PlanType);
        Assert.Equal(string.Empty, capturedConfig.ApiKey);
    }
}
