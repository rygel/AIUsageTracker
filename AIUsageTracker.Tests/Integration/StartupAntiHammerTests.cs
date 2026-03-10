// <copyright file="StartupAntiHammerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Linq;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Core;

public class StartupAntiHammerTests
{
    private sealed class TestableProviderRefreshService : ProviderRefreshService
    {
        public TestableProviderRefreshService(
            ILogger<ProviderRefreshService> logger,
            ILoggerFactory loggerFactory,
            IUsageDatabase database,
            INotificationService notificationService,
            IHttpClientFactory httpClientFactory,
            IConfigService configService,
            IAppPathProvider pathProvider,
            System.Collections.Generic.IEnumerable<IProviderService> providers,
            UsageAlertsService usageAlertsService,
            ProviderRefreshCircuitBreakerService providerCircuitBreakerService,
            IMonitorJobScheduler jobScheduler)
            : base(
                logger,
                loggerFactory,
                database,
                notificationService,
                httpClientFactory,
                configService,
                pathProvider,
                providers,
                usageAlertsService,
                providerCircuitBreakerService,
                jobScheduler)
        {
        }

        public List<(bool ForceAll, IReadOnlyCollection<string>? IncludeProviderIds)> TriggerCalls { get; } = [];

        public override Task TriggerRefreshAsync(
            bool forceAll = false,
            IReadOnlyCollection<string>? includeProviderIds = null,
            bool bypassCircuitBreaker = false)
        {
            this.TriggerCalls.Add((forceAll, includeProviderIds));
            return Task.CompletedTask;
        }

        public Task RunExecuteAsync(CancellationToken cancellationToken)
        {
            return this.ExecuteAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenDatabaseHasData_DoesNotTriggerFullRefreshAsync()
    {
        var mockLogger = new Mock<ILogger<ProviderRefreshService>>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockDb = new Mock<IUsageDatabase>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockConfigService = new Mock<IConfigService>();
        var mockPathProvider = new Mock<IAppPathProvider>();
        var mockUsageAlertsLogger = new Mock<ILogger<UsageAlertsService>>();
        var mockCircuitBreakerLogger = new Mock<ILogger<ProviderRefreshCircuitBreakerService>>();
        var mockJobScheduler = new Mock<IMonitorJobScheduler>();
        var usageAlertsService = new UsageAlertsService(
            mockUsageAlertsLogger.Object,
            mockDb.Object,
            mockNotificationService.Object,
            mockConfigService.Object);
        var providerCircuitBreakerService = new ProviderRefreshCircuitBreakerService(mockCircuitBreakerLogger.Object);

        mockDb.Setup(db => db.IsHistoryEmptyAsync())
            .ReturnsAsync(false);

        mockConfigService.Setup(cs => cs.ScanForKeysAsync())
            .ReturnsAsync(new List<ProviderConfig>());
        mockConfigService.Setup(cs => cs.GetPreferencesAsync())
            .ReturnsAsync(new AppPreferences());
        mockConfigService.Setup(cs => cs.GetConfigsAsync())
            .ReturnsAsync(new List<ProviderConfig>());

        mockJobScheduler.Setup(
            scheduler => scheduler.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns((string jobName, Func<CancellationToken, Task> work, MonitorJobPriority priority, string? coalesceKey) =>
            {
                work(CancellationToken.None).GetAwaiter().GetResult();
                return true;
            });

        var service = new TestableProviderRefreshService(
            mockLogger.Object,
            mockLoggerFactory.Object,
            mockDb.Object,
            mockNotificationService.Object,
            mockHttpClientFactory.Object,
            mockConfigService.Object,
            mockPathProvider.Object,
            Enumerable.Empty<IProviderService>(),
            usageAlertsService,
            providerCircuitBreakerService,
            mockJobScheduler.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await service.RunExecuteAsync(cts.Token);

        mockDb.Verify(db => db.IsHistoryEmptyAsync(), Times.Once);
        mockConfigService.Verify(cs => cs.ScanForKeysAsync(), Times.Never);
        Assert.Single(service.TriggerCalls);
        Assert.True(service.TriggerCalls[0].ForceAll);
        Assert.NotNull(service.TriggerCalls[0].IncludeProviderIds);
        Assert.Contains("antigravity", service.TriggerCalls[0].IncludeProviderIds!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDatabaseIsEmpty_TriggersFullRefreshAsync()
    {
        var mockLogger = new Mock<ILogger<ProviderRefreshService>>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockDb = new Mock<IUsageDatabase>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockConfigService = new Mock<IConfigService>();
        var mockPathProvider = new Mock<IAppPathProvider>();
        var mockUsageAlertsLogger = new Mock<ILogger<UsageAlertsService>>();
        var mockCircuitBreakerLogger = new Mock<ILogger<ProviderRefreshCircuitBreakerService>>();
        var mockJobScheduler = new Mock<IMonitorJobScheduler>();
        var usageAlertsService = new UsageAlertsService(
            mockUsageAlertsLogger.Object,
            mockDb.Object,
            mockNotificationService.Object,
            mockConfigService.Object);
        var providerCircuitBreakerService = new ProviderRefreshCircuitBreakerService(mockCircuitBreakerLogger.Object);

        mockDb.Setup(db => db.IsHistoryEmptyAsync())
            .ReturnsAsync(true);

        mockConfigService.Setup(cs => cs.ScanForKeysAsync())
            .ReturnsAsync(new List<ProviderConfig>())
            .Verifiable();
        mockConfigService.Setup(cs => cs.GetPreferencesAsync())
            .ReturnsAsync(new AppPreferences());
        mockConfigService.Setup(cs => cs.GetConfigsAsync())
            .ReturnsAsync(new List<ProviderConfig>());

        mockJobScheduler.Setup(
            scheduler => scheduler.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns((string jobName, Func<CancellationToken, Task> work, MonitorJobPriority priority, string? coalesceKey) =>
            {
                work(CancellationToken.None).GetAwaiter().GetResult();
                return true;
            });

        var service = new TestableProviderRefreshService(
            mockLogger.Object,
            mockLoggerFactory.Object,
            mockDb.Object,
            mockNotificationService.Object,
            mockHttpClientFactory.Object,
            mockConfigService.Object,
            mockPathProvider.Object,
            Enumerable.Empty<IProviderService>(),
            usageAlertsService,
            providerCircuitBreakerService,
            mockJobScheduler.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await service.RunExecuteAsync(cts.Token);

        mockDb.Verify(db => db.IsHistoryEmptyAsync(), Times.Once);
        mockConfigService.Verify(cs => cs.ScanForKeysAsync(), Times.Once);
        Assert.Single(service.TriggerCalls);
        Assert.True(service.TriggerCalls[0].ForceAll);
        Assert.Null(service.TriggerCalls[0].IncludeProviderIds);
    }

    [Fact]
    public void ProviderRefreshService_HasExecuteAsyncMethod()
    {
        var type = typeof(ProviderRefreshService);
        var executeMethod = type.GetMethod("ExecuteAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(executeMethod);
        Assert.True(
            executeMethod.ReturnType == typeof(Task),
            "ExecuteAsync should return Task");
    }

    [Fact]
    public void TriggerRefreshAsync_AcceptsIncludeProviderIdsParameter()
    {
        var type = typeof(ProviderRefreshService);
        var method = type.GetMethod("TriggerRefreshAsync");

        Assert.NotNull(method);
        var parameters = method.GetParameters();

        var includeProviderIdsParam = parameters.FirstOrDefault(p => string.Equals(p.Name, "includeProviderIds", StringComparison.Ordinal));
        Assert.NotNull(includeProviderIdsParam);
        Assert.True(
            includeProviderIdsParam.ParameterType == typeof(IReadOnlyCollection<string>),
            "includeProviderIds should be IReadOnlyCollection<string>");
    }
}
