// <copyright file="StartupSequenceServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class StartupSequenceServiceTests
{
    private readonly Mock<IConfigService> _configService = new();
    private readonly Mock<IMonitorJobScheduler> _jobScheduler = new();

    [Fact]
    public async Task QueueInitialDataSeeding_QueuedJobScansKeysAndRefreshesAllAsync()
    {
        this._configService.Setup(service => service.ScanForKeysAsync()).ReturnsAsync(new List<ProviderConfig>());
        Func<CancellationToken, Task>? queuedJob = null;
        this._jobScheduler
            .Setup(jobScheduler => jobScheduler.Enqueue(
                "startup-provider-seeding",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.High,
                "startup-provider-seeding"))
            .Callback<string, Func<CancellationToken, Task>, MonitorJobPriority, string?>(
                (_, job, _, _) => queuedJob = job)
            .Returns(true);

        var service = this.CreateService();
        var refreshCalls = 0;

        service.QueueInitialDataSeeding(_ =>
        {
            refreshCalls++;
            return Task.CompletedTask;
        });

        Assert.NotNull(queuedJob);
        await queuedJob!(CancellationToken.None);

        this._configService.Verify(configService => configService.ScanForKeysAsync(), Times.Once);
        Assert.Equal(1, refreshCalls);
    }

    [Fact]
    public async Task QueueInitialDataSeeding_WhenKeyScanFails_DoesNotRefreshAsync()
    {
        this._configService
            .Setup(service => service.ScanForKeysAsync())
            .ThrowsAsync(new InvalidOperationException("scan failed"));
        Func<CancellationToken, Task>? queuedJob = null;
        this._jobScheduler
            .Setup(jobScheduler => jobScheduler.Enqueue(
                "startup-provider-seeding",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.High,
                "startup-provider-seeding"))
            .Callback<string, Func<CancellationToken, Task>, MonitorJobPriority, string?>(
                (_, job, _, _) => queuedJob = job)
            .Returns(true);

        var service = this.CreateService();
        var refreshCalls = 0;

        service.QueueInitialDataSeeding(_ =>
        {
            refreshCalls++;
            return Task.CompletedTask;
        });

        Assert.NotNull(queuedJob);
        await queuedJob!(CancellationToken.None);

        Assert.Equal(0, refreshCalls);
    }

    [Fact]
    public async Task QueueStartupTargetedRefresh_QueuedJobUsesStartupProviderIdsAsync()
    {
        Func<CancellationToken, Task>? queuedJob = null;
        this._jobScheduler
            .Setup(jobScheduler => jobScheduler.Enqueue(
                "startup-targeted-provider-refresh",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.Low,
                "startup-targeted-provider-refresh"))
            .Callback<string, Func<CancellationToken, Task>, MonitorJobPriority, string?>(
                (_, job, _, _) => queuedJob = job)
            .Returns(true);

        var service = this.CreateService();
        IReadOnlyCollection<string>? providerIds = null;

        service.QueueStartupTargetedRefresh((ids, _) =>
        {
            providerIds = ids;
            return Task.CompletedTask;
        });

        Assert.NotNull(queuedJob);
        await queuedJob!(CancellationToken.None);

        Assert.Equal(ProviderMetadataCatalog.GetStartupRefreshProviderIds(), providerIds);
    }

    [Fact]
    public async Task QueueStartupTargetedRefresh_WhenRefreshFails_SwallowsExceptionAsync()
    {
        Func<CancellationToken, Task>? queuedJob = null;
        this._jobScheduler
            .Setup(jobScheduler => jobScheduler.Enqueue(
                "startup-targeted-provider-refresh",
                It.IsAny<Func<CancellationToken, Task>>(),
                MonitorJobPriority.Low,
                "startup-targeted-provider-refresh"))
            .Callback<string, Func<CancellationToken, Task>, MonitorJobPriority, string?>(
                (_, job, _, _) => queuedJob = job)
            .Returns(true);

        var service = this.CreateService();
        service.QueueStartupTargetedRefresh((_, _) => throw new InvalidOperationException("refresh failed"));

        Assert.NotNull(queuedJob);
        await queuedJob!(CancellationToken.None);
    }

    private StartupSequenceService CreateService()
    {
        var refreshJobScheduler = new ProviderRefreshJobScheduler(
            this._jobScheduler.Object,
            NullLogger<ProviderRefreshJobScheduler>.Instance);

        return new StartupSequenceService(
            refreshJobScheduler,
            this._configService.Object,
            new Mock<IAppPathProvider>().Object,
            NullLogger<StartupSequenceService>.Instance);
    }
}
