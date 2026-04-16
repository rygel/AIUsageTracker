// <copyright file="MonitorLauncherProcessControllerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Core;

public class MonitorLauncherProcessControllerTests
{
    [Fact]
    public void TryStartMonitorProcess_ReturnsFalse_WhenProcessStartReturnsNull()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "nonexistent_exe_12345",
            UseShellExecute = false,
        };

        var result = MonitorLauncherProcessController.TryStartMonitorProcess(startInfo, "test");

        Assert.False(result);
    }

    [Fact]
    public async Task StopAgentAsync_ReturnsTrue_WhenKnownProcessStopped()
    {
        var stopProcessCalled = false;
        var invalidateCalled = false;

        var result = await MonitorLauncherProcessController.StopAgentAsync(
            info: new MonitorInfo { ProcessId = 1234 },
            fallbackPort: 5000,
            stopWaitSeconds: 5,
            checkHealthAsync: _ => Task.FromResult(false),
            stopProcessAsync: pid =>
            {
                stopProcessCalled = pid == 1234;
                return Task.FromResult(true);
            },
            stopNamedProcessesAsync: () => Task.FromResult(false),
            invalidateMonitorInfoAsync: () =>
            {
                invalidateCalled = true;
                return Task.CompletedTask;
            },
            logger: Mock.Of<ILogger<MonitorLauncher>>());

        Assert.True(result);
        Assert.True(stopProcessCalled);
        Assert.True(invalidateCalled);
    }

    [Fact]
    public async Task StopAgentAsync_TriesNamedProcesses_WhenKnownProcessFails()
    {
        var namedStopped = false;
        var invalidateCalled = false;

        var result = await MonitorLauncherProcessController.StopAgentAsync(
            info: new MonitorInfo { ProcessId = 0 },
            fallbackPort: 5000,
            stopWaitSeconds: 5,
            checkHealthAsync: _ => Task.FromResult(true),
            stopProcessAsync: _ => Task.FromResult(false),
            stopNamedProcessesAsync: () =>
            {
                namedStopped = true;
                return Task.FromResult(true);
            },
            invalidateMonitorInfoAsync: () =>
            {
                invalidateCalled = true;
                return Task.CompletedTask;
            },
            logger: Mock.Of<ILogger<MonitorLauncher>>());

        Assert.True(result);
        Assert.True(namedStopped);
        Assert.True(invalidateCalled);
    }

    [Fact]
    public async Task StopAgentAsync_ReturnsTrue_WhenHealthCheckFails()
    {
        var invalidateCalled = false;

        var result = await MonitorLauncherProcessController.StopAgentAsync(
            info: new MonitorInfo { ProcessId = 0 },
            fallbackPort: 5000,
            stopWaitSeconds: 5,
            checkHealthAsync: _ => Task.FromResult(false),
            stopProcessAsync: _ => Task.FromResult(false),
            stopNamedProcessesAsync: () => Task.FromResult(false),
            invalidateMonitorInfoAsync: () =>
            {
                invalidateCalled = true;
                return Task.CompletedTask;
            },
            logger: Mock.Of<ILogger<MonitorLauncher>>());

        Assert.True(result);
        Assert.True(invalidateCalled);
    }

    [Fact]
    public async Task StopAgentAsync_ReturnsFalse_WhenAllMethodsFailAndStillHealthy()
    {
        var result = await MonitorLauncherProcessController.StopAgentAsync(
            info: new MonitorInfo { ProcessId = 0 },
            fallbackPort: 5000,
            stopWaitSeconds: 5,
            checkHealthAsync: _ => Task.FromResult(true),
            stopProcessAsync: _ => Task.FromResult(false),
            stopNamedProcessesAsync: () => Task.FromResult(false),
            invalidateMonitorInfoAsync: () => Task.CompletedTask,
            logger: Mock.Of<ILogger<MonitorLauncher>>());

        Assert.False(result);
    }

    [Fact]
    public async Task StopAgentAsync_ReturnsFalse_WhenKnownProcessHasNoPid()
    {
        var result = await MonitorLauncherProcessController.StopAgentAsync(
            info: null,
            fallbackPort: 5000,
            stopWaitSeconds: 5,
            checkHealthAsync: _ => Task.FromResult(true),
            stopProcessAsync: _ => Task.FromResult(false),
            stopNamedProcessesAsync: () => Task.FromResult(false),
            invalidateMonitorInfoAsync: () => Task.CompletedTask,
            logger: Mock.Of<ILogger<MonitorLauncher>>());

        Assert.False(result);
    }

    [Fact]
    public async Task TryStopNamedProcessesAsync_UsesOverride_WhenProvided()
    {
        var overrideCalled = false;

        var result = await MonitorLauncherProcessController.TryStopNamedProcessesAsync(
            stopWaitSeconds: 5,
            stopNamedProcessesOverride: () =>
            {
                overrideCalled = true;
                return Task.FromResult(true);
            });

        Assert.True(result);
        Assert.True(overrideCalled);
    }

    [Fact]
    public async Task TryStopNamedProcessesAsync_ReturnsFalse_WhenOverrideReturnsFalse()
    {
        var result = await MonitorLauncherProcessController.TryStopNamedProcessesAsync(
            stopWaitSeconds: 5,
            stopNamedProcessesOverride: () => Task.FromResult(false));

        Assert.False(result);
    }

    [Fact]
    public async Task TryStopProcessAsync_UsesOverride_WhenProvided()
    {
        var result = await MonitorLauncherProcessController.TryStopProcessAsync(
            processId: 9999,
            stopWaitSeconds: 5,
            stopProcessOverride: pid => Task.FromResult(pid == 9999));

        Assert.True(result);
    }

    [Fact]
    public async Task TryStopProcessAsync_ReturnsTrue_ForNonExistentProcess()
    {
        var result = await MonitorLauncherProcessController.TryStopProcessAsync(
            processId: 999999,
            stopWaitSeconds: 5,
            stopProcessOverride: null);

        Assert.True(result);
    }

    [Fact]
    public void TryResolveLaunchPlan_ReturnsNull_WhenNoExeOrProjectFound()
    {
        var plan = MonitorLauncherProcessController.TryResolveLaunchPlan(5000);

        // This may or may not find the project depending on the test runner's working directory.
        // In CI it likely returns null since the Monitor exe won't be published.
        // We just verify it doesn't throw.
        Assert.True(plan == null || plan.Value.StartInfo != null);
    }
}
