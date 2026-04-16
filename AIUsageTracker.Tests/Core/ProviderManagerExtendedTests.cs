// <copyright file="ProviderManagerExtendedTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Core;

public class ProviderManagerExtendedTests
{
    private readonly Mock<ILogger<ProviderManager>> _mockLogger;
    private readonly Mock<IConfigLoader> _mockConfigLoader;

    public ProviderManagerExtendedTests()
    {
        this._mockLogger = new Mock<ILogger<ProviderManager>>();
        this._mockConfigLoader = new Mock<IConfigLoader>();
    }

    [Fact]
    public async Task GetAllUsageAsync_ReturnsCachedOnSecondCallWithoutForce()
    {
        var providers = new List<IProviderService>
        {
            MockProviderService.CreateOpenAIMock(),
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        this._mockConfigLoader.Setup(cl => cl.LoadConfigAsync()).ReturnsAsync(configs);

        using var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        var first = await manager.GetAllUsageAsync(forceRefresh: true);
        var second = await manager.GetAllUsageAsync(forceRefresh: false);

        Assert.Equal(first.Count, second.Count);
    }

    [Fact]
    public async Task GetUsageAsync_ThrowsOnUnknownProvider()
    {
        var providers = new List<IProviderService>
        {
            MockProviderService.CreateOpenAIMock(),
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        this._mockConfigLoader.Setup(cl => cl.LoadConfigAsync()).ReturnsAsync(configs);

        using var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.GetUsageAsync("nonexistent"));
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUsageForKnownProvider()
    {
        var providers = new List<IProviderService>
        {
            MockProviderService.CreateOpenAIMock(),
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        this._mockConfigLoader.Setup(cl => cl.LoadConfigAsync()).ReturnsAsync(configs);

        using var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        var result = await manager.GetUsageAsync("openai");

        Assert.NotEmpty(result);
        Assert.Contains(result, u => u.ProviderId == "openai");
    }

    [Fact]
    public async Task GetAllUsageAsync_WithOverrideConfigs_UsesOverrides()
    {
        var providers = new List<IProviderService>
        {
            MockProviderService.CreateOpenAIMock(),
        };

        var baseConfigs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "gemini" },
        };

        this._mockConfigLoader.Setup(cl => cl.LoadConfigAsync()).ReturnsAsync(baseConfigs);

        using var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        var overrideConfigs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        var result = await manager.GetAllUsageAsync(
            forceRefresh: true,
            overrideConfigs: overrideConfigs);

        Assert.Contains(result, u => u.ProviderId == "openai");
    }

    [Fact]
    public async Task GetAllUsageAsync_WithProgressCallback_InvokesCallback()
    {
        var providers = new List<IProviderService>
        {
            MockProviderService.CreateOpenAIMock(),
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        this._mockConfigLoader.Setup(cl => cl.LoadConfigAsync()).ReturnsAsync(configs);

        using var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        var callbackInvoked = false;
        var result = await manager.GetAllUsageAsync(
            forceRefresh: true,
            progressCallback: _ => callbackInvoked = true);

        Assert.True(callbackInvoked);
    }

    [Fact]
    public async Task GetAllUsageAsync_HandlesProviderTimeout()
    {
        var mockProvider = new MockProviderService
        {
            ProviderId = "slow-provider",
            UsageHandler = _ => Task.Delay(TimeSpan.FromSeconds(30))
                .ContinueWith(_ => (IEnumerable<ProviderUsage>)new[]
                {
                    new ProviderUsage { ProviderId = "slow-provider", IsAvailable = true },
                }),
        };

        var providers = new List<IProviderService> { mockProvider };
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "slow-provider" },
        };

        this._mockConfigLoader.Setup(cl => cl.LoadConfigAsync()).ReturnsAsync(configs);

        using var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        var result = await manager.GetAllUsageAsync(forceRefresh: true);

        Assert.NotEmpty(result);
        Assert.Contains(result, u => u.ProviderId == "slow-provider");
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var manager = new ProviderManager(
            providers: [],
            configLoader: this._mockConfigLoader.Object,
            logger: this._mockLogger.Object);

        manager.Dispose();
        var ex = Record.Exception(() => manager.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void LastUsages_ReturnsEmpty_Initially()
    {
        using var manager = new ProviderManager(
            providers: [],
            configLoader: this._mockConfigLoader.Object,
            logger: this._mockLogger.Object);

        Assert.Empty(manager.LastUsages);
    }

    [Fact]
    public void LastConfigs_ReturnsNull_Initially()
    {
        using var manager = new ProviderManager(
            providers: [],
            configLoader: this._mockConfigLoader.Object,
            logger: this._mockLogger.Object);

        Assert.Null(manager.LastConfigs);
    }

    [Fact]
    public async Task GetConfigsAsync_ReturnsConfigsFromFile()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        this._mockConfigLoader.Setup(cl => cl.LoadConfigAsync()).ReturnsAsync(configs);

        using var manager = new ProviderManager(
            providers: [],
            configLoader: this._mockConfigLoader.Object,
            logger: this._mockLogger.Object);

        var result = await manager.GetConfigsAsync(forceRefresh: true);

        Assert.Single(result);
        Assert.Equal("openai", result[0].ProviderId);
    }
}
