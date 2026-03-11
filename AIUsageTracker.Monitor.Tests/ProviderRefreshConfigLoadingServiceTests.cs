// <copyright file="ProviderRefreshConfigLoadingServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshConfigLoadingServiceTests
{
    private readonly Mock<IConfigService> _configService = new();
    private readonly Mock<IUsageDatabase> _database = new();

    [Fact]
    public async Task LoadConfigsForRefreshAsync_AddsAutoIncludedProvidersAndFiltersToActiveConfigsAsync()
    {
        this._configService.Setup(service => service.GetConfigsAsync()).ReturnsAsync(new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "codex", ApiKey = "session-token" },
        });

        var service = this.CreateService(CreateProvider("antigravity", autoIncludeWhenUnconfigured: true));

        var (configs, activeConfigs) = await service.LoadConfigsForRefreshAsync(forceAll: false, includeProviderIds: null);

        Assert.Equal(
            new[] { "antigravity", "codex", "openai" },
            configs.Select(config => config.ProviderId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "antigravity", "codex" },
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
            new() { ProviderId = "codex", ApiKey = "codex-session" },
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
            new() { ProviderId = "openai", ApiKey = "key-1" },
            new() { ProviderId = "codex", ApiKey = "key-2" },
        };
        var service = this.CreateService();

        await service.PersistConfiguredProvidersAsync(configs);

        this._database.Verify(database => database.StoreProviderAsync(configs[0], null), Times.Once);
        this._database.Verify(database => database.StoreProviderAsync(configs[1], null), Times.Once);
    }

    private static IProviderService CreateProvider(
        string providerId,
        bool autoIncludeWhenUnconfigured = false)
    {
        var mock = new Mock<IProviderService>();
        mock.SetupGet(provider => provider.ProviderId).Returns(providerId);
        mock.SetupGet(provider => provider.Definition).Returns(
            new ProviderDefinition(
                providerId,
                displayName: providerId,
                planType: PlanType.Coding,
                isQuotaBased: true,
                defaultConfigType: "quota-based",
                autoIncludeWhenUnconfigured: autoIncludeWhenUnconfigured));
        mock.Setup(provider => provider.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(Array.Empty<ProviderUsage>());
        return mock.Object;
    }

    private ProviderRefreshConfigLoadingService CreateService(params IProviderService[] providers)
    {
        var selector = new ProviderRefreshConfigSelector(
            providers,
            NullLogger<ProviderRefreshConfigSelector>.Instance);

        return new ProviderRefreshConfigLoadingService(
            this._configService.Object,
            this._database.Object,
            selector,
            NullLogger<ProviderRefreshConfigLoadingService>.Instance);
    }
}
