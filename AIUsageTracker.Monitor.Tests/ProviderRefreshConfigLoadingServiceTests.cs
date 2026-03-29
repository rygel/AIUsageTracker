// <copyright file="ProviderRefreshConfigLoadingServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshConfigLoadingServiceTests
{
    private static readonly string TestApiKey1 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey2 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey3 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey4 = Guid.NewGuid().ToString();

    private readonly Mock<IConfigService> _configService = new();
    private readonly Mock<IUsageDatabase> _database = new();

    [Fact]
    public async Task LoadConfigsForRefreshAsync_IncludesOnlyKeyedConfigsAsync()
    {
        this._configService.Setup(service => service.GetConfigsAsync()).ReturnsAsync(new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "codex", ApiKey = TestApiKey1 },
        });

        var service = this.CreateService();

        var (configs, activeConfigs) = await service.LoadConfigsForRefreshAsync(forceAll: false, includeProviderIds: null);

        Assert.Equal(
            new[] { "codex", "openai" },
            configs.Select(config => config.ProviderId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "codex" },
            activeConfigs.Select(config => config.ProviderId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task LoadConfigsForRefreshAsync_WhenForceAllTrue_ReturnsConfigsWithoutApiKeysAsync()
    {
        this._configService.Setup(service => service.GetConfigsAsync()).ReturnsAsync(new List<ProviderConfig>
        {
            new() { ProviderId = "codex" },
        });

        var service = this.CreateService();

        var (_, activeConfigs) = await service.LoadConfigsForRefreshAsync(forceAll: true, includeProviderIds: null);

        Assert.Equal(new[] { "codex" }, activeConfigs.Select(config => config.ProviderId).ToArray());
    }

    [Fact]
    public async Task LoadConfigsForRefreshAsync_WhenIncludedProviderIdsProvided_FiltersActiveConfigsAsync()
    {
        this._configService.Setup(service => service.GetConfigsAsync()).ReturnsAsync(new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = TestApiKey2 },
        });

        var service = this.CreateService();

        var (_, activeConfigs) = await service.LoadConfigsForRefreshAsync(
            forceAll: false,
            includeProviderIds: new[] { "codex" });

        var activeConfig = Assert.Single(activeConfigs);
        Assert.Equal("codex", activeConfig.ProviderId);
    }

    [Fact]
    public async Task PersistConfiguredProvidersAsync_StoresEachConfigAsync()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai", ApiKey = TestApiKey3 },
            new() { ProviderId = "codex", ApiKey = TestApiKey4 },
        };
        var service = this.CreateService();

        await service.PersistConfiguredProvidersAsync(configs);

        this._database.Verify(database => database.StoreProviderAsync(configs[0], null), Times.Once);
        this._database.Verify(database => database.StoreProviderAsync(configs[1], null), Times.Once);
    }

    private ProviderRefreshConfigLoadingService CreateService()
    {
        var selector = new ProviderRefreshConfigSelector();

        return new ProviderRefreshConfigLoadingService(
            this._configService.Object,
            this._database.Object,
            selector,
            NullLogger<ProviderRefreshConfigLoadingService>.Instance);
    }
}
