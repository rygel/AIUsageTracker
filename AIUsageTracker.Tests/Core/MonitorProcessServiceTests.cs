// <copyright file="MonitorProcessServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Core;

public sealed class MonitorProcessServiceTests
{
    [Fact]
    public async Task GetAgentStatusDetailedAsync_ReturnsMissing_WhenMonitorInfoIsAbsentAsync()
    {
        var lifecycle = new FakeMonitorLifecycleService
        {
            StatusSequence =
            [
                new MonitorAgentStatus
                {
                    IsRunning = false,
                    Port = 5000,
                    Message = "Monitor info file not found.",
                    Error = "agent-info-missing",
                },
            ],
        };

        var service = CreateService(lifecycle);
        var result = await service.GetAgentStatusDetailedAsync();

        Assert.False(result.IsRunning);
        Assert.Equal(5000, result.Port);
        Assert.Equal("agent-info-missing", result.Error);
    }

    [Fact]
    public async Task GetAgentStatusDetailedAsync_ReturnsUnreachable_WhenMetadataIsStaleAsync()
    {
        var lifecycle = new FakeMonitorLifecycleService
        {
            StatusSequence =
            [
                new MonitorAgentStatus
                {
                    IsRunning = false,
                    Port = 6111,
                    Message = "Monitor metadata exists but endpoint is not reachable.",
                    Error = "monitor-unreachable",
                },
            ],
        };

        var service = CreateService(lifecycle);
        var result = await service.GetAgentStatusDetailedAsync();

        Assert.False(result.IsRunning);
        Assert.Equal(6111, result.Port);
        Assert.Equal("monitor-unreachable", result.Error);
    }

    [Fact]
    public async Task StartAgentDetailedAsync_ReturnsAlreadyRunning_WhenMonitorIsHealthyAsync()
    {
        var lifecycle = new FakeMonitorLifecycleService
        {
            StatusSequence =
            [
                new MonitorAgentStatus
                {
                    IsRunning = true,
                    Port = 6222,
                    Message = "Monitor healthy.",
                },
            ],
        };

        var service = CreateService(lifecycle);
        var result = await service.StartAgentDetailedAsync();

        Assert.True(result.Success);
        Assert.Equal("Monitor already running on port 6222.", result.Message);
        Assert.Equal(0, lifecycle.EnsureAgentRunningCalls);
    }

    [Fact]
    public async Task GetAgentStatusDetailedAsync_ReturnsStarting_WhenMonitorStartupIsInProgressAsync()
    {
        var lifecycle = new FakeMonitorLifecycleService
        {
            StatusSequence =
            [
                new MonitorAgentStatus
                {
                    IsRunning = false,
                    Port = 5000,
                    Message = "Monitor is starting.",
                    Error = "monitor-starting",
                },
            ],
        };

        var service = CreateService(lifecycle);
        var result = await service.GetAgentStatusDetailedAsync();

        Assert.False(result.IsRunning);
        Assert.Equal(5000, result.Port);
        Assert.Equal("monitor-starting", result.Error);
        Assert.Equal("Monitor is starting.", result.Message);
    }

    [Fact]
    public async Task StopAgentDetailedAsync_ReturnsAlreadyStopped_WhenMonitorInfoIsAbsentAsync()
    {
        var lifecycle = new FakeMonitorLifecycleService
        {
            StatusSequence =
            [
                new MonitorAgentStatus
                {
                    IsRunning = false,
                    Port = 5000,
                    Message = "Monitor info file not found.",
                    Error = "agent-info-missing",
                },
            ],
        };

        var service = CreateService(lifecycle);
        var result = await service.StopAgentDetailedAsync();

        Assert.True(result.Success);
        Assert.Equal("Monitor already stopped (info file missing).", result.Message);
        Assert.Equal(0, lifecycle.StopAgentCalls);
    }

    private static MonitorProcessService CreateService(FakeMonitorLifecycleService lifecycle)
    {
        return new MonitorProcessService(lifecycle, NullLogger<MonitorProcessService>.Instance);
    }

    private sealed class FakeMonitorLifecycleService : IMonitorLifecycleService
    {
        public List<MonitorAgentStatus> StatusSequence { get; init; } = [];

        public bool EnsureAgentRunningResult { get; init; } = true;

        public bool StopAgentResult { get; init; } = true;

        public int EnsureAgentRunningCalls { get; private set; }

        public int StopAgentCalls { get; private set; }

        public Task<bool> StartAgentAsync() => Task.FromResult(true);

        public Task<bool> StopAgentAsync()
        {
            this.StopAgentCalls++;
            return Task.FromResult(this.StopAgentResult);
        }

        public Task<bool> EnsureAgentRunningAsync()
        {
            this.EnsureAgentRunningCalls++;
            return Task.FromResult(this.EnsureAgentRunningResult);
        }

        public Task<bool> WaitForAgentAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> GetAgentPortAsync() => Task.FromResult(5000);

        public Task<bool> IsAgentRunningAsync() => Task.FromResult(false);

        public Task<(bool IsRunning, int Port)> IsAgentRunningWithPortAsync() => Task.FromResult((false, 5000));

        public Task<MonitorAgentStatus> GetAgentStatusInfoAsync()
        {
            if (this.StatusSequence.Count == 0)
            {
                return Task.FromResult(new MonitorAgentStatus
                {
                    IsRunning = false,
                    Port = 5000,
                    Message = "No status configured.",
                    Error = "test-status-missing",
                });
            }

            var status = this.StatusSequence[0];
            if (this.StatusSequence.Count > 1)
            {
                this.StatusSequence.RemoveAt(0);
            }

            return Task.FromResult(status);
        }

        public Task<MonitorMetadataStatus> GetMonitorMetadataSnapshotAsync()
        {
            return Task.FromResult(new MonitorMetadataStatus
            {
                IsUsable = false,
                Info = null,
            });
        }
    }
}
