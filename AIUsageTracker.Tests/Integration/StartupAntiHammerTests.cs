using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq;
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
            System.Collections.Generic.IEnumerable<IProviderService> providers)
            : base(logger, loggerFactory, database, notificationService, httpClientFactory, configService, providers)
        {
        }

        public List<(bool ForceAll, IReadOnlyCollection<string>? IncludeProviderIds)> TriggerCalls { get; } = [];

        public override Task TriggerRefreshAsync(
            bool forceAll = false,
            IReadOnlyCollection<string>? includeProviderIds = null,
            bool bypassCircuitBreaker = false)
        {
            TriggerCalls.Add((forceAll, includeProviderIds));
            return Task.CompletedTask;
        }

        public Task RunExecuteAsync(CancellationToken cancellationToken)
        {
            return ExecuteAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenDatabaseHasData_DoesNotTriggerFullRefresh()
    {
        var mockLogger = new Mock<ILogger<ProviderRefreshService>>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockDb = new Mock<IUsageDatabase>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockConfigService = new Mock<IConfigService>();

        mockDb.Setup(db => db.IsHistoryEmptyAsync())
            .ReturnsAsync(false);

        mockConfigService.Setup(cs => cs.ScanForKeysAsync())
            .ReturnsAsync(new List<ProviderConfig>());

        var service = new TestableProviderRefreshService(
            mockLogger.Object,
            mockLoggerFactory.Object,
            mockDb.Object,
            mockNotificationService.Object,
            mockHttpClientFactory.Object,
            mockConfigService.Object,
            Enumerable.Empty<IProviderService>());

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
    public async Task ExecuteAsync_WhenDatabaseIsEmpty_TriggersFullRefresh()
    {
        var mockLogger = new Mock<ILogger<ProviderRefreshService>>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockDb = new Mock<IUsageDatabase>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockConfigService = new Mock<IConfigService>();

        mockDb.Setup(db => db.IsHistoryEmptyAsync())
            .ReturnsAsync(true);

        mockConfigService.Setup(cs => cs.ScanForKeysAsync())
            .ReturnsAsync(new List<ProviderConfig>())
            .Verifiable();

        var service = new TestableProviderRefreshService(
            mockLogger.Object,
            mockLoggerFactory.Object,
            mockDb.Object,
            mockNotificationService.Object,
            mockHttpClientFactory.Object,
            mockConfigService.Object,
            Enumerable.Empty<IProviderService>());

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
        Assert.True(executeMethod.ReturnType == typeof(Task), 
            "ExecuteAsync should return Task");
    }

    [Fact]
    public void TriggerRefreshAsync_AcceptsIncludeProviderIdsParameter()
    {
        var type = typeof(ProviderRefreshService);
        var method = type.GetMethod("TriggerRefreshAsync");
        
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        
        var includeProviderIdsParam = parameters.FirstOrDefault(p => p.Name == "includeProviderIds");
        Assert.NotNull(includeProviderIdsParam);
        Assert.True(includeProviderIdsParam.ParameterType == typeof(IReadOnlyCollection<string>),
            "includeProviderIds should be IReadOnlyCollection<string>");
    }
}
