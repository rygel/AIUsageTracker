// <copyright file="PowerStateListenerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class PowerStateListenerTests
{
    [Fact]
    public void OnSuspend_PausesScheduler()
    {
        var schedulerLogger = new Mock<ILogger<MonitorJobScheduler>>();
        var listenerLogger = new Mock<ILogger<PowerStateListener>>();
        var pathProvider = new Mock<AIUsageTracker.Core.Interfaces.IAppPathProvider>();
        pathProvider.Setup(p => p.GetMonitorInfoFilePath()).Returns(Path.Combine(Path.GetTempPath(), $"power-test-{Guid.NewGuid()}", "monitor-info.json"));

        var scheduler = new MonitorJobScheduler(schedulerLogger.Object);

        var listener = new PowerStateListener(
            listenerLogger.Object,
            scheduler,
            pathProvider.Object,
            onSuspend: () => scheduler.Pause(),
            onResume: () => scheduler.Resume());

        listener.SimulateSuspend();

        Assert.True(scheduler.GetSnapshot().IsPaused);
    }

    [Fact]
    public void OnResume_ResumesScheduler()
    {
        var schedulerLogger = new Mock<ILogger<MonitorJobScheduler>>();
        var listenerLogger = new Mock<ILogger<PowerStateListener>>();
        var pathProvider = new Mock<AIUsageTracker.Core.Interfaces.IAppPathProvider>();
        pathProvider.Setup(p => p.GetMonitorInfoFilePath()).Returns(Path.Combine(Path.GetTempPath(), $"power-test-{Guid.NewGuid()}", "monitor-info.json"));

        var scheduler = new MonitorJobScheduler(schedulerLogger.Object);

        var listener = new PowerStateListener(
            listenerLogger.Object,
            scheduler,
            pathProvider.Object,
            onSuspend: () => scheduler.Pause(),
            onResume: () => scheduler.Resume());

        listener.SimulateSuspend();
        Assert.True(scheduler.GetSnapshot().IsPaused);

        listener.SimulateResume();
        Assert.False(scheduler.GetSnapshot().IsPaused);
    }
}
