// <copyright file="ProviderManagerLifecycleServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderManagerLifecycleServiceTests
{
    [Fact]
    public void CurrentManager_BeforeInitialize_IsNull()
    {
        var service = CreateService();

        Assert.Null(service.CurrentManager);
        Assert.Equal(ProviderManager.DefaultMaxConcurrentProviderRequests, service.CurrentMaxConcurrency);
    }

    [Fact]
    public void Initialize_CreatesManagerAndTracksConcurrency()
    {
        var service = CreateService();

        service.Initialize(4);

        Assert.NotNull(service.CurrentManager);
        Assert.Equal(4, service.CurrentMaxConcurrency);
    }

    [Fact]
    public async Task EnsureConcurrencyAsync_WhenConfigurationUnchanged_PreservesManagerInstanceAsync()
    {
        var preferences = new AppPreferences { MaxConcurrentProviderRequests = 4 };
        var service = CreateService(preferences);
        service.Initialize(4);
        var originalManager = service.CurrentManager;

        await service.EnsureConcurrencyAsync();

        Assert.Same(originalManager, service.CurrentManager);
        Assert.Equal(4, service.CurrentMaxConcurrency);
    }

    [Fact]
    public async Task EnsureConcurrencyAsync_WhenConfigurationChanges_ReinitializesManagerAsync()
    {
        var preferences = new AppPreferences { MaxConcurrentProviderRequests = 6 };
        var service = CreateService(preferences);
        service.Initialize(4);
        var originalManager = service.CurrentManager;

        preferences.MaxConcurrentProviderRequests = 2;
        await service.EnsureConcurrencyAsync();

        Assert.NotSame(originalManager, service.CurrentManager);
        Assert.Equal(2, service.CurrentMaxConcurrency);
    }

    private static ProviderManagerLifecycleService CreateService(AppPreferences? preferences = null)
    {
        var configService = new Mock<IConfigService>();
        var currentPreferences = preferences ?? new AppPreferences();
        configService.Setup(service => service.GetPreferencesAsync()).ReturnsAsync(() => currentPreferences);

        return new ProviderManagerLifecycleService(
            NullLogger<ProviderManagerLifecycleService>.Instance,
            NullLoggerFactory.Instance,
            configService.Object,
            Mock.Of<IAppPathProvider>(),
            Array.Empty<IProviderService>());
    }
}
