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

        var result = await manager.GetAllUsageAsync().ConfigureAwait(false);

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

        var result = await manager.GetAllUsageAsync().ConfigureAwait(false);

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

        var result = await manager.GetAllUsageAsync(includeProviderIds: new[] { "openai" }).ConfigureAwait(false);

        Assert.Contains(result, usage => string.Equals(usage.ProviderId, "openai", StringComparison.Ordinal));
        Assert.DoesNotContain(result, usage => string.Equals(usage.ProviderId, "gemini", StringComparison.Ordinal));
    }
}
