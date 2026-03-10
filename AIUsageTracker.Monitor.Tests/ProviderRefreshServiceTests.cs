// <copyright file="ProviderRefreshServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
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
            this._mockHubContext.Object);
    }

    [Fact]
    public async Task TriggerRefreshAsync_BroadcastsSignalRMessages()
    {
        // Arrange
        this._mockDatabase.Setup(d => d.IsHistoryEmptyAsync()).ReturnsAsync(false);
        this._mockConfigService.Setup(c => c.GetConfigsAsync()).ReturnsAsync(new List<ProviderConfig>());

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
}
