// <copyright file="ProviderRefreshServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Monitor.Hubs;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
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
        this._mockLoggerFactory
            .Setup(factory => factory.CreateLogger(It.IsAny<string>()))
            .Returns(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
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
    public async Task TriggerRefreshAsync_BroadcastsSignalRMessagesAsync()
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
        Assert.Null(telemetry.LastRefreshAttemptUtc);
        Assert.Null(telemetry.LastRefreshCompletedUtc);
        Assert.Null(telemetry.LastSuccessfulRefreshUtc);
        Assert.Null(telemetry.LastError);
        Assert.Empty(telemetry.ProviderDiagnostics);
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
        Assert.NotNull(telemetry.LastRefreshAttemptUtc);
        Assert.NotNull(telemetry.LastRefreshCompletedUtc);
        Assert.Null(telemetry.LastSuccessfulRefreshUtc);
        Assert.Equal("ProviderManager not ready", telemetry.LastError);
        Assert.Empty(telemetry.ProviderDiagnostics);
    }

    [Fact]
    public async Task TriggerRefreshAsync_WhenRefreshSucceeds_RecordsSuccessTelemetryAndProviderDiagnosticsAsync()
    {
        var scenario = CreatePipelinePrivacyScenario();
        InvokeInitializeProviders(scenario.Service, 6);
        try
        {
            await scenario.Service.TriggerRefreshAsync(forceAll: true);
        }
        finally
        {
            TestTempPaths.CleanupPath(scenario.Files.Root);
        }

        var telemetry = scenario.Service.GetRefreshTelemetrySnapshot();

        Assert.Equal(1, telemetry.RefreshCount);
        Assert.Equal(1, telemetry.RefreshSuccessCount);
        Assert.Equal(0, telemetry.RefreshFailureCount);
        Assert.NotNull(telemetry.LastRefreshAttemptUtc);
        Assert.NotNull(telemetry.LastRefreshCompletedUtc);
        Assert.NotNull(telemetry.LastSuccessfulRefreshUtc);
        Assert.Null(telemetry.LastError);

        var providerDiagnostic = Assert.Single(telemetry.ProviderDiagnostics);
        Assert.Equal("openai", providerDiagnostic.ProviderId);
        Assert.NotNull(providerDiagnostic.LastRefreshAttemptUtc);
        Assert.NotNull(providerDiagnostic.LastSuccessfulRefreshUtc);
        Assert.Null(providerDiagnostic.LastRefreshError);
        Assert.Equal(0, providerDiagnostic.ConsecutiveFailures);
        Assert.False(providerDiagnostic.IsCircuitOpen);
    }

    [Fact]
    public async Task TriggerRefreshAsync_EmitsRefreshActivityWithErrorStatusWhenProviderManagerMissingAsync()
    {
        var stoppedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, MonitorActivitySources.RefreshSourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stoppedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        await this._service.TriggerRefreshAsync();

        var refreshActivity = Assert.Single(
            stoppedActivities,
            activity => string.Equals(activity.OperationName, "monitor.provider_refresh", StringComparison.Ordinal));
        Assert.Equal(ActivityStatusCode.Error, refreshActivity.Status);
        Assert.Equal(false, refreshActivity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, "refresh.force_all", StringComparison.Ordinal)).Value);
        Assert.Equal(0, refreshActivity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, "refresh.include_provider_ids.count", StringComparison.Ordinal)).Value);
        Assert.Equal(false, refreshActivity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, "refresh.bypass_circuit_breaker", StringComparison.Ordinal)).Value);
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
                "manual-provider-refresh"),
            Times.Once);
    }

    [Fact]
    public void QueueManualRefresh_WithScopedRequest_UsesRequestAwareCoalesceKey()
    {
        this._mockJobScheduler
            .Setup(s => s.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);

        var queued = this._service.QueueManualRefresh(
            includeProviderIds: new[] { "zai", "OpenAI" },
            bypassCircuitBreaker: true);

        Assert.True(queued);
        this._mockJobScheduler.Verify(
            s => s.Enqueue(
                "manual-provider-refresh",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.High,
                "manual-provider-refresh|forceAll=False|bypass=True|include=openai,zai"),
            Times.Once);
    }

    [Fact]
    public void BuildManualRefreshCoalesceKey_EquivalentProviderScopes_ProducesSameKey()
    {
        var first = ProviderRefreshService.BuildManualRefreshCoalesceKey(
            forceAll: false,
            includeProviderIds: new[] { "OpenAI", "zai" },
            bypassCircuitBreaker: true);
        var second = ProviderRefreshService.BuildManualRefreshCoalesceKey(
            forceAll: false,
            includeProviderIds: new[] { "ZAI", "openai", "openai" },
            bypassCircuitBreaker: true);

        Assert.Equal(first, second);
        Assert.Equal("manual-provider-refresh|forceAll=False|bypass=True|include=openai,zai", first);
    }

    [Fact]
    public void BuildManualRefreshCoalesceKey_DefaultManualRefresh_ReturnsNull()
    {
        var key = ProviderRefreshService.BuildManualRefreshCoalesceKey(
            forceAll: false,
            includeProviderIds: null,
            bypassCircuitBreaker: false);

        Assert.Null(key);
    }

    [Fact]
    public async Task QueueForceRefresh_QueuedWorkBypassesCircuitBreakerAsync()
    {
        Func<CancellationToken, Task>? queuedWork = null;
        this._mockJobScheduler
            .Setup(s => s.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Callback<string, Func<CancellationToken, Task>, MonitorJobPriority, string?>((_, work, _, _) => queuedWork = work)
            .Returns(true);

        var stoppedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, MonitorActivitySources.RefreshSourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stoppedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var queued = this._service.QueueForceRefresh(forceAll: true);
        Assert.True(queued);
        Assert.NotNull(queuedWork);

        await queuedWork!(CancellationToken.None);

        var refreshActivity = Assert.Single(
            stoppedActivities,
            activity => string.Equals(activity.OperationName, "monitor.provider_refresh", StringComparison.Ordinal));
        Assert.Equal(true, refreshActivity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, "refresh.force_all", StringComparison.Ordinal)).Value);
        Assert.Equal(true, refreshActivity.TagObjects.FirstOrDefault(tag => string.Equals(tag.Key, "refresh.bypass_circuit_breaker", StringComparison.Ordinal)).Value);
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
        var scenario = CreatePipelinePrivacyScenario();
        InvokeInitializeProviders(scenario.Service, 6);
        try
        {
            await scenario.Service.TriggerRefreshAsync(forceAll: true);
        }
        finally
        {
            TestTempPaths.CleanupPath(scenario.Files.Root);
        }

        scenario.Pipeline.Verify(
            p => p.Process(
                It.IsAny<IEnumerable<ProviderUsage>>(),
                It.Is<IReadOnlyCollection<string>>(ids => ids.Contains("openai", StringComparer.OrdinalIgnoreCase)),
                true),
            Times.Once);

        scenario.Database.Verify(
            d => d.StoreHistoryAsync(It.Is<IEnumerable<ProviderUsage>>(items =>
                items.Any(u => u.ProviderId == "openai" && Math.Abs(u.RequestsPercentage - 50) < 0.001))),
            Times.Once);
    }

    [Fact]
    public async Task CheckProviderAsync_UsesPipelineOutputAndReturnsConnectedAsync()
    {
        var scenario = CreatePipelinePrivacyScenario();
        InvokeInitializeProviders(scenario.Service, 6);
        try
        {
            var (success, message, status) = await scenario.Service.CheckProviderAsync("openai");

            Assert.True(success);
            Assert.Equal("Connected", message);
            Assert.Equal(200, status);
            scenario.Pipeline.Verify(
                p => p.Process(
                    It.IsAny<IEnumerable<ProviderUsage>>(),
                    It.Is<IReadOnlyCollection<string>>(ids => ids.Contains("openai", StringComparer.OrdinalIgnoreCase)),
                    true),
                Times.Once);
        }
        finally
        {
            TestTempPaths.CleanupPath(scenario.Files.Root);
        }
    }

    [Fact]
    public async Task CheckProviderAsync_WhenPipelineMarksUnavailable_ReturnsServiceUnavailableAsync()
    {
        var scenario = CreatePipelinePrivacyScenario();
        scenario.Pipeline.Setup(
                p => p.Process(
                    It.IsAny<IEnumerable<ProviderUsage>>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    true))
            .Returns(new ProviderUsageProcessingResult
            {
                Usages = new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = "openai",
                        ProviderName = "OpenAI",
                        IsAvailable = false,
                        Description = "Invalid detail contract",
                    },
                },
            });

        InvokeInitializeProviders(scenario.Service, 6);
        try
        {
            var (success, message, status) = await scenario.Service.CheckProviderAsync("openai");

            Assert.False(success);
            Assert.Equal("Invalid detail contract", message);
            Assert.Equal(503, status);
        }
        finally
        {
            TestTempPaths.CleanupPath(scenario.Files.Root);
        }
    }

    private static PipelinePrivacyScenario CreatePipelinePrivacyScenario()
    {
        var files = CreatePipelineTestFiles();
        var loggerFactory = CreateLoggerFactory();
        var logger = new Mock<ILogger<ProviderRefreshService>>();
        var database = new Mock<IUsageDatabase>();
        var notificationService = new Mock<INotificationService>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var configService = new Mock<IConfigService>();
        var pathProvider = new Mock<IAppPathProvider>();
        var jobScheduler = new Mock<IMonitorJobScheduler>();
        var pipeline = new Mock<IProviderUsageProcessingPipeline>();
        var provider = CreateOpenAiProvider();
        ConfigurePipelinePrivacyMocks(files, database, configService, pathProvider, jobScheduler, pipeline);
        var service = CreatePipelinePrivacyService(
            logger.Object,
            loggerFactory.Object,
            database.Object,
            notificationService.Object,
            httpClientFactory.Object,
            configService.Object,
            pathProvider.Object,
            provider.Object,
            pipeline.Object);

        return new PipelinePrivacyScenario(service, database, pipeline, files);
    }

    private static Mock<ILoggerFactory> CreateLoggerFactory()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory
            .Setup(factory => factory.CreateLogger(It.IsAny<string>()))
            .Returns(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return loggerFactory;
    }

    private static void ConfigurePipelinePrivacyMocks(
        PipelineTestFiles files,
        Mock<IUsageDatabase> database,
        Mock<IConfigService> configService,
        Mock<IAppPathProvider> pathProvider,
        Mock<IMonitorJobScheduler> jobScheduler,
        Mock<IProviderUsageProcessingPipeline> pipeline)
    {
        var preferences = new AppPreferences { IsPrivacyMode = true, MaxConcurrentProviderRequests = 6 };
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai", ApiKey = "test-key", Type = "pay-as-you-go" },
        };
        var processedOutput = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = "OpenAI",
            RequestsUsed = 5,
            RequestsAvailable = 10,
            RequestsPercentage = 50,
            IsAvailable = true,
        };

        configService.Setup(c => c.GetPreferencesAsync()).ReturnsAsync(preferences);
        configService.Setup(c => c.GetConfigsAsync()).ReturnsAsync(configs);
        pathProvider.Setup(p => p.GetAppDataRoot()).Returns(files.Root);
        pathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(files.Root, "usage.db"));
        pathProvider.Setup(p => p.GetLogDirectory()).Returns(files.Root);
        pathProvider.Setup(p => p.GetAuthFilePath()).Returns(files.AuthPath);
        pathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(files.ProvidersPath);
        pathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(files.PreferencesPath);
        pathProvider.Setup(p => p.GetUserProfileRoot()).Returns(files.Root);
        database.Setup(d => d.GetRecentHistoryAsync(It.IsAny<int>())).ReturnsAsync(new List<ProviderUsage>());
        jobScheduler.Setup(s => s.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);
        pipeline.Setup(p => p.Process(It.IsAny<IEnumerable<ProviderUsage>>(), It.IsAny<IReadOnlyCollection<string>>(), true))
            .Returns(new ProviderUsageProcessingResult { Usages = new[] { processedOutput } });
    }

    private static Mock<IProviderService> CreateOpenAiProvider()
    {
        var providerDefinition = new ProviderDefinition(
            providerId: "openai",
            displayName: "OpenAI",
            planType: PlanType.Usage,
            isQuotaBased: false,
            defaultConfigType: "pay-as-you-go",
            autoIncludeWhenUnconfigured: true);
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
        return provider;
    }

    private static ProviderRefreshService CreatePipelinePrivacyService(
        ILogger<ProviderRefreshService> logger,
        ILoggerFactory loggerFactory,
        IUsageDatabase database,
        INotificationService notificationService,
        IHttpClientFactory httpClientFactory,
        IConfigService configService,
        IAppPathProvider pathProvider,
        IProviderService provider,
        IProviderUsageProcessingPipeline pipeline)
    {
        var alertsLogger = new Mock<ILogger<UsageAlertsService>>();
        var usageAlertsService = new UsageAlertsService(alertsLogger.Object, database, notificationService, configService);
        var circuitBreakerLogger = new Mock<ILogger<ProviderRefreshCircuitBreakerService>>();
        var circuitBreakerService = new ProviderRefreshCircuitBreakerService(circuitBreakerLogger.Object);
        var jobScheduler = new Mock<IMonitorJobScheduler>();
        jobScheduler.Setup(s => s.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);

        return new ProviderRefreshService(
            logger,
            loggerFactory,
            database,
            notificationService,
            httpClientFactory,
            configService,
            pathProvider,
            new[] { provider },
            usageAlertsService,
            circuitBreakerService,
            jobScheduler.Object,
            pipeline);
    }

    private static PipelineTestFiles CreatePipelineTestFiles()
    {
        var root = TestTempPaths.CreateDirectory("provider-refresh-test");
        var authPath = Path.Combine(root, "auth.json");
        var providersPath = Path.Combine(root, "providers.json");
        var preferencesPath = Path.Combine(root, "preferences.json");
        var authJson = """
        {
          "openai": {
            "key": "test-key",
            "type": "pay-as-you-go"
          }
        }
        """;
        File.WriteAllText(authPath, authJson);
        File.WriteAllText(providersPath, "{}");
        File.WriteAllText(preferencesPath, "{}");
        return new PipelineTestFiles(root, authPath, providersPath, preferencesPath);
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
        var lifecycleField = typeof(ProviderRefreshService).GetField(
            "_providerManagerLifecycle",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lifecycleField);

        var lifecycle = Assert.IsType<ProviderManagerLifecycleService>(lifecycleField!.GetValue(service));
        return lifecycle.CurrentMaxConcurrency;
    }

    private sealed record PipelineTestFiles(string Root, string AuthPath, string ProvidersPath, string PreferencesPath);

    private sealed record PipelinePrivacyScenario(
        ProviderRefreshService Service,
        Mock<IUsageDatabase> Database,
        Mock<IProviderUsageProcessingPipeline> Pipeline,
        PipelineTestFiles Files);
}
