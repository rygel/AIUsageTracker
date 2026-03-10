// <copyright file="ProviderRefreshJobSchedulerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshJobSchedulerTests
{
    private readonly Mock<IMonitorJobScheduler> _jobScheduler = new();

    [Fact]
    public void RegisterRecurringRefresh_UsesExpectedRecurringJobSettings()
    {
        var scheduler = this.CreateScheduler();
        var interval = TimeSpan.FromMinutes(5);

        scheduler.RegisterRecurringRefresh(interval, _ => Task.CompletedTask);

        this._jobScheduler.Verify(
            jobScheduler => jobScheduler.RegisterRecurringJob(
                "scheduled-provider-refresh",
                interval,
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.Low,
                interval,
                "scheduled-provider-refresh"),
            Times.Once);
    }

    [Fact]
    public void QueueManualRefresh_UsesHighPriorityWithoutCoalesceKey()
    {
        var scheduler = this.CreateScheduler();
        this._jobScheduler
            .Setup(jobScheduler => jobScheduler.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);

        var queued = scheduler.QueueManualRefresh(_ => Task.CompletedTask);

        Assert.True(queued);
        this._jobScheduler.Verify(
            jobScheduler => jobScheduler.Enqueue(
                "manual-provider-refresh",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.High,
                null),
            Times.Once);
    }

    [Fact]
    public void QueueInitialDataSeeding_UsesHighPriorityWithCoalesceKey()
    {
        var scheduler = this.CreateScheduler();
        this._jobScheduler
            .Setup(jobScheduler => jobScheduler.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);

        var queued = scheduler.QueueInitialDataSeeding(_ => Task.CompletedTask);

        Assert.True(queued);
        this._jobScheduler.Verify(
            jobScheduler => jobScheduler.Enqueue(
                "startup-provider-seeding",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.High,
                "startup-provider-seeding"),
            Times.Once);
    }

    [Fact]
    public void QueueStartupTargetedRefresh_UsesLowPriorityWithCoalesceKey()
    {
        var scheduler = this.CreateScheduler();
        this._jobScheduler
            .Setup(jobScheduler => jobScheduler.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);

        var queued = scheduler.QueueStartupTargetedRefresh(_ => Task.CompletedTask);

        Assert.True(queued);
        this._jobScheduler.Verify(
            jobScheduler => jobScheduler.Enqueue(
                "startup-targeted-provider-refresh",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.Low,
                "startup-targeted-provider-refresh"),
            Times.Once);
    }

    private ProviderRefreshJobScheduler CreateScheduler()
    {
        return new ProviderRefreshJobScheduler(this._jobScheduler.Object, NullLogger<ProviderRefreshJobScheduler>.Instance);
    }
}
