// <copyright file="MonitorLifecycleTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Tests.Core;

[Collection("MonitorLifecycle")]
public class MonitorLifecycleTests
{
    private static readonly TimeSpan StartStopTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WaitReadyTimeout = TimeSpan.FromSeconds(40);

    private static async Task<T> WithTimeoutAsync<T>(Task<T> task, TimeSpan timeout, string operation)
    {
        try
        {
            return await task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Operation '{operation}' exceeded {timeout.TotalSeconds:F0}s.", ex);
        }
    }

    private static async Task WithTimeoutAsync(Task task, TimeSpan timeout, string operation)
    {
        try
        {
            await task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Operation '{operation}' exceeded {timeout.TotalSeconds:F0}s.", ex);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MonitorLifecycle_StartStopRestart_WorksAsync()
    {
        if (!IsIntegrationEnabled())
        {
            return;
        }

        await WithTimeoutAsync(
            RunLifecycleScenarioAsync(),
            TimeSpan.FromSeconds(90),
            "Monitor lifecycle integration test");
    }

    private static bool IsIntegrationEnabled()
    {
        var value = Environment.GetEnvironmentVariable("RUN_MONITOR_LIFECYCLE_TESTS");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RunLifecycleScenarioAsync()
    {
        try
        {
            var canStart = await WithTimeoutAsync(
                MonitorLauncher.StartAgentAsync(),
                StartStopTimeout,
                "StartAgentAsync").ConfigureAwait(false);
            if (!canStart)
            {
                return;
            }

            using var initialWaitTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var started = await WithTimeoutAsync(
                MonitorLauncher.WaitForAgentAsync(initialWaitTokenSource.Token),
                WaitReadyTimeout,
                "WaitForAgentAsync (initial)").ConfigureAwait(false);
            if (!started)
            {
                return;
            }

            await WithTimeoutAsync(
                MonitorLauncher.StopAgentAsync(),
                StartStopTimeout,
                "StopAgentAsync").ConfigureAwait(false);

            var restarted = await WithTimeoutAsync(
                MonitorLauncher.StartAgentAsync(),
                StartStopTimeout,
                "StartAgentAsync (restart)").ConfigureAwait(false);
            if (!restarted)
            {
                return;
            }

            using var restartWaitTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var restartReady = await WithTimeoutAsync(
                MonitorLauncher.WaitForAgentAsync(restartWaitTokenSource.Token),
                WaitReadyTimeout,
                "WaitForAgentAsync (restart)").ConfigureAwait(false);
            Assert.True(restartReady, "Monitor should be reachable after restart.");
        }
        finally
        {
            // Ensure the test never leaves a monitor process running in CI.
            await WithTimeoutAsync(
                MonitorLauncher.StopAgentAsync(),
                StartStopTimeout,
                "StopAgentAsync (cleanup)").ConfigureAwait(false);
        }
    }
}
