// <copyright file="ProviderRefreshServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Monitor.Hubs;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshServiceTests
{
    private readonly Mock<ILogger<ProviderRefreshService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<IUsageDatabase> _mockDatabase;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IAppPathProvider> _mockPathProvider;
    private readonly Mock<IHubContext<UsageHub>> _mockHubContext;
    private readonly Mock<IConfigService> _mockConfigService;
    private readonly Mock<IMonitorJobScheduler> _mockJobScheduler;
    private readonly UsageAlertsService _usageAlertsService;
    private readonly ProviderRefreshCircuitBreakerService _providerRefreshCircuitBreakerService;
    private readonly ProviderRefreshService _service;

    public ProviderRefreshServiceTests()
    {
        this._mockLogger = new Mock<ILogger<ProviderRefreshService>>();
        this._mockLoggerFactory = new Mock<ILoggerFactory>();
        this._mockDatabase = new Mock<IUsageDatabase>();
        this._mockNotificationService = new Mock<INotificationService>();
        this._mockHttpClientFactory = new Mock<IHttpClientFactory>();
        this._mockPathProvider = new Mock<IAppPathProvider>();
        this._mockHubContext = new Mock<IHubContext<UsageHub>>();
        this._mockConfigService = new Mock<IConfigService>();
        this._mockJobScheduler = new Mock<IMonitorJobScheduler>();
        this._mockDatabase.Setup(d => d.IsHistoryEmptyAsync()).ReturnsAsync(false);
        this._mockConfigService.Setup(c => c.GetPreferencesAsync()).ReturnsAsync(new AppPreferences());
        this._mockConfigService.Setup(c => c.GetConfigsAsync()).ReturnsAsync(new List<ProviderConfig>());
        this._mockJobScheduler
            .Setup(s => s.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);

        // Setup HubContext mock
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        this._mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var alertsLogger = new Mock<ILogger<UsageAlertsService>>();
        this._usageAlertsService = new UsageAlertsService(
            alertsLogger.Object,
            this._mockDatabase.Object,
            this._mockNotificationService.Object,
            this._mockConfigService.Object);
        var circuitBreakerLogger = new Mock<ILogger<ProviderRefreshCircuitBreakerService>>();
        this._providerRefreshCircuitBreakerService = new ProviderRefreshCircuitBreakerService(circuitBreakerLogger.Object);

        this._service = new ProviderRefreshService(
            this._mockLogger.Object,
            this._mockLoggerFactory.Object,
            this._mockDatabase.Object,
            this._mockNotificationService.Object,
            this._mockHttpClientFactory.Object,
            this._mockConfigService.Object,
            this._mockPathProvider.Object,
            Enumerable.Empty<IProviderService>(),
            this._usageAlertsService,
            this._providerRefreshCircuitBreakerService,
            this._mockJobScheduler.Object,
            hubContext: this._mockHubContext.Object);
    }

    [Fact]
    public async Task TriggerRefreshAsync_BroadcastsSignalRMessages()
    {
        // Arrange
        this._mockConfigService.Setup(c => c.GetConfigsAsync()).ReturnsAsync(new List<ProviderConfig>());
        var initializeProviders = typeof(ProviderRefreshService).GetMethod(
            "InitializeProviders",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(initializeProviders);
        initializeProviders!.Invoke(this._service, new object[] { 6 });

        // Act
        await this._service.TriggerRefreshAsync();

        // Assert
        var mockClients = Mock.Get(this._mockHubContext.Object.Clients);
        var mockClientProxy = Mock.Get(mockClients.Object.All);

        mockClientProxy.Verify(
            c => c.SendCoreAsync("RefreshStarted", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);

        mockClientProxy.Verify(
            c => c.SendCoreAsync("UsageUpdated", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void GetRefreshTelemetrySnapshot_InitialState_IsZeroed()
    {
        var telemetry = this._service.GetRefreshTelemetrySnapshot();

        Assert.Equal(0, telemetry.RefreshCount);
        Assert.Equal(0, telemetry.RefreshSuccessCount);
        Assert.Equal(0, telemetry.RefreshFailureCount);
        Assert.Equal(0, telemetry.ErrorRatePercent);
        Assert.Equal(0, telemetry.AverageLatencyMs);
        Assert.Null(telemetry.LastError);
    }

    [Fact]
    public async Task TriggerRefreshAsync_WhenProviderManagerMissing_RecordsFailureTelemetryAsync()
    {
        await this._service.TriggerRefreshAsync();
        var telemetry = this._service.GetRefreshTelemetrySnapshot();

        Assert.Equal(1, telemetry.RefreshCount);
        Assert.Equal(0, telemetry.RefreshSuccessCount);
        Assert.Equal(1, telemetry.RefreshFailureCount);
        Assert.True(telemetry.ErrorRatePercent > 0);
        Assert.Equal("ProviderManager not ready", telemetry.LastError);
    }

    [Fact]
    public void QueueManualRefresh_UsesHighPriorityScheduler()
    {
        this._mockJobScheduler
            .Setup(s => s.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);

        var queued = this._service.QueueManualRefresh();

        Assert.True(queued);
        this._mockJobScheduler.Verify(
            s => s.Enqueue(
                "manual-provider-refresh",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.High,
                null),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenHistoryEmpty_QueuesStartupSeedingAndRecurringRefreshAsync()
    {
        this._mockDatabase.Setup(d => d.IsHistoryEmptyAsync()).ReturnsAsync(true);

        await this._service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await this._service.StopAsync(CancellationToken.None);

        this._mockJobScheduler.Verify(
            s => s.RegisterRecurringJob(
                "scheduled-provider-refresh",
                It.IsAny<TimeSpan>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.Low,
                It.IsAny<TimeSpan?>(),
                "scheduled-provider-refresh"),
            Times.Once);

        this._mockJobScheduler.Verify(
            s => s.Enqueue(
                "startup-provider-seeding",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.High,
                "startup-provider-seeding"),
            Times.Once);

        this._mockJobScheduler.Verify(
            s => s.Enqueue(
                "startup-targeted-provider-refresh",
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenHistoryExists_QueuesTargetedRefreshAndRecurringRefreshAsync()
    {
        this._mockDatabase.Setup(d => d.IsHistoryEmptyAsync()).ReturnsAsync(false);

        await this._service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await this._service.StopAsync(CancellationToken.None);

        this._mockJobScheduler.Verify(
            s => s.RegisterRecurringJob(
                "scheduled-provider-refresh",
                It.IsAny<TimeSpan>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.Low,
                It.IsAny<TimeSpan?>(),
                "scheduled-provider-refresh"),
            Times.Once);

        this._mockJobScheduler.Verify(
            s => s.Enqueue(
                "startup-targeted-provider-refresh",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.Low,
                "startup-targeted-provider-refresh"),
            Times.Once);

        this._mockJobScheduler.Verify(
            s => s.Enqueue(
                "startup-provider-seeding",
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task TriggerRefreshAsync_WhenConcurrencyPreferenceChanges_ReinitializesProviderManagerAsync()
    {
        var preferences = new AppPreferences { MaxConcurrentProviderRequests = 6 };
        this._mockConfigService.Setup(c => c.GetPreferencesAsync()).ReturnsAsync(() => preferences);

        InvokeInitializeProviders(this._service, 6);
        Assert.Equal(6, GetProviderManagerConcurrency(this._service));

        await this._service.TriggerRefreshAsync();

        preferences.MaxConcurrentProviderRequests = 2;
        await this._service.TriggerRefreshAsync();

        Assert.Equal(2, GetProviderManagerConcurrency(this._service));
    }

    [Fact]
    public async Task TriggerRefreshAsync_UsesPipelinePrivacyFlagAndPersistsPipelineOutputAsync()
    {
        var logger = new Mock<ILogger<ProviderRefreshService>>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory
            .Setup(factory => factory.CreateLogger(It.IsAny<string>()))
            .Returns(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var database = new Mock<IUsageDatabase>();
        var notificationService = new Mock<INotificationService>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var configService = new Mock<IConfigService>();
        var pathProvider = new Mock<IAppPathProvider>();
        var jobScheduler = new Mock<IMonitorJobScheduler>();
        var pipeline = new Mock<IProviderUsageProcessingPipeline>();
        var testRoot = Path.Combine(Path.GetTempPath(), $"provider-refresh-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        var authPath = Path.Combine(testRoot, "auth.json");
        var providersPath = Path.Combine(testRoot, "providers.json");
        var preferencesPath = Path.Combine(testRoot, "preferences.json");
        await File.WriteAllTextAsync(authPath, """
        {
          "openai": {
            "key": "test-key",
            "type": "pay-as-you-go"
          }
        }
        """);
        await File.WriteAllTextAsync(providersPath, "{}");
        await File.WriteAllTextAsync(preferencesPath, "{}");

        var preferences = new AppPreferences
        {
            IsPrivacyMode = true,
            MaxConcurrentProviderRequests = 6,
        };

        var configs = new List<ProviderConfig>
        {
            new()
            {
                ProviderId = "openai",
                ApiKey = "test-key",
                Type = "pay-as-you-go",
            },
        };

        var providerDefinition = new ProviderDefinition(
            providerId: "openai",
            displayName: "OpenAI",
            planType: PlanType.Usage,
            isQuotaBased: false,
            defaultConfigType: "pay-as-you-go");

        var provider = new Mock<IProviderService>();
        provider.SetupGet(p => p.ProviderId).Returns("openai");
        provider.SetupGet(p => p.Definition).Returns(providerDefinition);
        provider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[]
            {
                new ProviderUsage
                {
                    ProviderId = "openai",
                    ProviderName = "OpenAI",
                    RequestsUsed = 2,
                    RequestsAvailable = 10,
                    RequestsPercentage = 20,
                    IsAvailable = true,
                },
            });

        var processedOutput = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = "OpenAI",
            RequestsUsed = 5,
            RequestsAvailable = 10,
            RequestsPercentage = 50,
            IsAvailable = true,
        };

        pipeline.Setup(p => p.Process(
                It.IsAny<IEnumerable<ProviderUsage>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                true))
            .Returns(new ProviderUsageProcessingResult
            {
                Usages = new[] { processedOutput },
            });

        configService.Setup(c => c.GetPreferencesAsync()).ReturnsAsync(preferences);
        configService.Setup(c => c.GetConfigsAsync()).ReturnsAsync(configs);
        pathProvider.Setup(p => p.GetAppDataRoot()).Returns(testRoot);
        pathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(testRoot, "usage.db"));
        pathProvider.Setup(p => p.GetLogDirectory()).Returns(testRoot);
        pathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        pathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        pathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(preferencesPath);
        pathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);
        database.Setup(d => d.GetRecentHistoryAsync(It.IsAny<int>())).ReturnsAsync(new List<ProviderUsage>());
        jobScheduler
            .Setup(s => s.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);

        var alertsLogger = new Mock<ILogger<UsageAlertsService>>();
        var usageAlertsService = new UsageAlertsService(
            alertsLogger.Object,
            database.Object,
            notificationService.Object,
            configService.Object);

        var circuitBreakerLogger = new Mock<ILogger<ProviderRefreshCircuitBreakerService>>();
        var circuitBreakerService = new ProviderRefreshCircuitBreakerService(circuitBreakerLogger.Object);

        var service = new ProviderRefreshService(
            logger.Object,
            loggerFactory.Object,
            database.Object,
            notificationService.Object,
            httpClientFactory.Object,
            configService.Object,
            pathProvider.Object,
            new[] { provider.Object },
            usageAlertsService,
            circuitBreakerService,
            jobScheduler.Object,
            pipeline.Object);

        InvokeInitializeProviders(service, 6);
        try
        {
            await service.TriggerRefreshAsync(forceAll: true);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }

        pipeline.Verify(
            p => p.Process(
                It.IsAny<IEnumerable<ProviderUsage>>(),
                It.Is<IReadOnlyCollection<string>>(ids => ids.Contains("openai", StringComparer.OrdinalIgnoreCase)),
                true),
            Times.Once);

        database.Verify(
            d => d.StoreHistoryAsync(It.Is<IEnumerable<ProviderUsage>>(items =>
                items.Any(u => u.ProviderId == "openai" && Math.Abs(u.RequestsPercentage - 50) < 0.001))),
            Times.Once);
    }

    private static void InvokeInitializeProviders(ProviderRefreshService service, int maxConcurrentRequests)
    {
        var initializeProviders = typeof(ProviderRefreshService).GetMethod(
            "InitializeProviders",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(initializeProviders);
        initializeProviders!.Invoke(service, new object[] { maxConcurrentRequests });
    }

    private static int GetProviderManagerConcurrency(ProviderRefreshService service)
    {
        var providerManagerField = typeof(ProviderRefreshService).GetField(
            "_providerManager",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(providerManagerField);

        var manager = providerManagerField!.GetValue(service) as ProviderManager;
        Assert.NotNull(manager);
        return manager!.MaxConcurrentProviderRequests;
    }
}
