// <copyright file="MonitorStartupOrchestratorWatchdogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Core;

public sealed class MonitorStartupOrchestratorWatchdogTests
{
    private static readonly TimeSpan BaseInterval = TimeSpan.FromSeconds(60);

    private readonly Mock<IMonitorService> _mockMonitorService;
    private readonly Mock<IMonitorLauncher> _mockLauncher;
    private readonly MonitorLifecycleService _lifecycleService;
    private readonly MonitorStartupOrchestrator _orchestrator;

    public MonitorStartupOrchestratorWatchdogTests()
    {
        this._mockMonitorService = new Mock<IMonitorService>();
        this._mockLauncher = new Mock<IMonitorLauncher>();
        this._lifecycleService = new MonitorLifecycleService(this._mockLauncher.Object);
        this._orchestrator = new MonitorStartupOrchestrator(
            this._mockMonitorService.Object,
            this._lifecycleService,
            NullLogger<MonitorStartupOrchestrator>.Instance);
    }

    [Fact]
    public async Task Watchdog_WhenHealthCheckFails_CallsEnsureAgentRunning()
    {
        this._mockMonitorService
            .Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        this._mockMonitorService
            .Setup(m => m.RefreshPortAsync())
            .Returns(Task.CompletedTask);
        this._mockLauncher
            .Setup(l => l.EnsureAgentRunningAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await this._orchestrator.RunWatchdogTickAsync();

        this._mockLauncher.Verify(l => l.EnsureAgentRunningAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Watchdog_WhenHealthCheckSucceeds_DoesNotRelaunch()
    {
        this._mockMonitorService
            .Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        await this._orchestrator.RunWatchdogTickAsync();

        this._mockLauncher.Verify(l => l.EnsureAgentRunningAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Watchdog_AfterRepeatedFailures_IncreasesInterval()
    {
        this._mockMonitorService
            .Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        this._mockMonitorService
            .Setup(m => m.RefreshPortAsync())
            .Returns(Task.CompletedTask);
        this._mockLauncher
            .Setup(l => l.EnsureAgentRunningAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await this._orchestrator.RunWatchdogTickAsync();
        var firstInterval = this._orchestrator.CurrentWatchdogInterval;

        await this._orchestrator.RunWatchdogTickAsync();
        var secondInterval = this._orchestrator.CurrentWatchdogInterval;

        Assert.True(secondInterval > firstInterval, $"Expected second interval ({secondInterval}) > first ({firstInterval})");
    }

    [Fact]
    public async Task Watchdog_AfterRecovery_ResetsInterval()
    {
        this._mockMonitorService
            .Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        this._mockMonitorService
            .Setup(m => m.RefreshPortAsync())
            .Returns(Task.CompletedTask);
        this._mockLauncher
            .Setup(l => l.EnsureAgentRunningAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Tick twice to grow the interval
        await this._orchestrator.RunWatchdogTickAsync();
        await this._orchestrator.RunWatchdogTickAsync();
        Assert.True(this._orchestrator.CurrentWatchdogInterval > BaseInterval);

        // Now health check succeeds — recovery
        this._mockMonitorService
            .Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        await this._orchestrator.RunWatchdogTickAsync();

        Assert.Equal(BaseInterval, this._orchestrator.CurrentWatchdogInterval);
    }

    [Fact]
    public async Task NotifyResumed_TriggersImmediateCheck()
    {
        this._mockMonitorService
            .Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        this._orchestrator.NotifyResumed();
        await this._orchestrator.RunWatchdogTickAsync();

        this._mockMonitorService.Verify(m => m.CheckHealthAsync(It.IsAny<TimeSpan>()), Times.Once);
    }
}
