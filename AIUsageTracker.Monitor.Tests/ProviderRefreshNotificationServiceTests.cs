// <copyright file="ProviderRefreshNotificationServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

#pragma warning disable CS0618 // UsedPercent: legacy field set in test fixtures

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Hubs;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshNotificationServiceTests
{
    [Fact]
    public async Task NotifyRefreshStartedAsync_WhenHubContextPresent_BroadcastsMessageAsync()
    {
        var hubContext = CreateHubContext(out _, out var clientProxy);
        var service = CreateService(hubContext.Object);

        await service.NotifyRefreshStartedAsync();

        clientProxy.Verify(
            proxy => proxy.SendCoreAsync("RefreshStarted", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyUsageUpdatedAsync_WhenHubContextPresent_BroadcastsMessageAsync()
    {
        var hubContext = CreateHubContext(out _, out var clientProxy);
        var service = CreateService(hubContext.Object);

        await service.NotifyUsageUpdatedAsync();

        clientProxy.Verify(
            proxy => proxy.SendCoreAsync("UsageUpdated", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyRefreshStartedAsync_WhenHubContextMissing_DoesNotThrowAsync()
    {
        var service = CreateService();

        var exception = await Record.ExceptionAsync(async () =>
        {
#pragma warning disable MA0004
            await service.NotifyRefreshStartedAsync();
            await service.NotifyUsageUpdatedAsync();
#pragma warning restore MA0004
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task ProcessUsageAlertsAsync_DelegatesResetDetectionAndUsageAlertsAsync()
    {
        var scenario = CreateAlertScenario();

        await scenario.Service.ProcessUsageAlertsAsync(scenario.Usages, scenario.Preferences, scenario.Configs);

        scenario.Database.Verify(
            d => d.StoreResetEventAsync("openai", "OpenAI", 95, 10, "usage"),
            Times.Once);
        scenario.NotificationService.Verify(notification => notification.ShowQuotaExceeded("OpenAI", "Usage reset detected."), Times.Once);
        scenario.NotificationService.Verify(notification => notification.ShowUsageAlert("OpenAI", 90), Times.Once);
    }

    private static ProviderRefreshNotificationService CreateService(IHubContext<UsageHub>? hubContext = null)
    {
        var alertsService = new UsageAlertsService(
            NullLogger<UsageAlertsService>.Instance,
            Mock.Of<IUsageDatabase>(),
            Mock.Of<INotificationService>(),
            Mock.Of<IConfigService>());
        return new ProviderRefreshNotificationService(alertsService, hubContext);
    }

    private static Mock<IHubContext<UsageHub>> CreateHubContext(out Mock<IHubClients> clients, out Mock<IClientProxy> clientProxy)
    {
        clients = new Mock<IHubClients>();
        clientProxy = new Mock<IClientProxy>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<UsageHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);
        return hubContext;
    }

    private static AlertScenario CreateAlertScenario()
    {
        var database = new Mock<IUsageDatabase>();
        database.Setup(d => d.GetRecentHistoryAsync(2)).ReturnsAsync(CreateRecentUsageHistory());

        var notificationService = new Mock<INotificationService>();
        var configService = new Mock<IConfigService>();
        configService.Setup(service => service.GetPreferencesAsync()).ReturnsAsync(CreateNotificationPreferences());
        configService.Setup(service => service.GetConfigsAsync()).ReturnsAsync(CreateNotifiableConfigs());

        var alertsService = new UsageAlertsService(
            NullLogger<UsageAlertsService>.Instance,
            database.Object,
            notificationService.Object,
            configService.Object);

        return new AlertScenario(
            new ProviderRefreshNotificationService(alertsService),
            database,
            notificationService,
            CreateUsageThresholdPreferences(),
            CreateNotifiableConfigs(),
            CreateCurrentUsage());
    }

    private static List<ProviderUsage> CreateRecentUsageHistory()
    {
        return
        [
            new()
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                RequestsUsed = 10,
                RequestsAvailable = 100,
                IsAvailable = true,
            },
            new()
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                RequestsUsed = 95,
                RequestsAvailable = 100,
                IsAvailable = true,
            },
        ];
    }

    private static List<ProviderUsage> CreateCurrentUsage()
    {
        return
        [
            new()
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                RequestsUsed = 90,
                RequestsAvailable = 100,
                UsedPercent = 90,
                IsAvailable = true,
            },
        ];
    }

    private static List<ProviderConfig> CreateNotifiableConfigs()
    {
        return
        [
            new()
            {
                ProviderId = "openai",
                EnableNotifications = true,
            },
        ];
    }

    private static AppPreferences CreateNotificationPreferences()
    {
        return new AppPreferences
        {
            EnableNotifications = true,
            NotifyOnQuotaExceeded = true,
        };
    }

    private static AppPreferences CreateUsageThresholdPreferences()
    {
        return new AppPreferences
        {
            EnableNotifications = true,
            NotifyOnUsageThreshold = true,
            NotificationThreshold = 80,
        };
    }

    private sealed record AlertScenario(
        ProviderRefreshNotificationService Service,
        Mock<IUsageDatabase> Database,
        Mock<INotificationService> NotificationService,
        AppPreferences Preferences,
        List<ProviderConfig> Configs,
        List<ProviderUsage> Usages);
}
