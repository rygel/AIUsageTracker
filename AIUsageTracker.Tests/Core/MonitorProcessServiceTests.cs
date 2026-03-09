// <copyright file="MonitorProcessServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Core;

[Collection("MonitorStartupPath")]
public sealed class MonitorProcessServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public MonitorProcessServiceTests()
    {
        this._tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "monitor-process-service-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempDirectory);
    }

    [Fact]
    public async Task GetAgentStatusDetailedAsync_ReturnsMissing_WhenMonitorInfoIsAbsentAsync()
    {
        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: Array.Empty<string>(),
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var service = this.CreateService();

        var result = await service.GetAgentStatusDetailedAsync().ConfigureAwait(false);

        Assert.False(result.IsRunning);
        Assert.Equal(5000, result.Port);
        Assert.Equal("agent-info-missing", result.Error);
    }

    [Fact]
    public async Task GetAgentStatusDetailedAsync_ReturnsUnreachable_WhenStaleMonitorInfoIsQuarantinedAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 6111,
            ProcessId = 7777,
        }).ConfigureAwait(false);

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var service = this.CreateService();

        var result = await service.GetAgentStatusDetailedAsync().ConfigureAwait(false);

        Assert.False(result.IsRunning);
        Assert.Equal(6111, result.Port);
        Assert.Equal("monitor-unreachable", result.Error);
    }

    [Fact]
    public async Task StartAgentDetailedAsync_ReturnsAlreadyRunning_WhenMonitorIsHealthyAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 6222,
            ProcessId = 8888,
        }).ConfigureAwait(false);

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 6222),
            processRunningAsync: processId => Task.FromResult(processId == 8888));

        var service = this.CreateService();

        var result = await service.StartAgentDetailedAsync().ConfigureAwait(false);

        Assert.True(result.Success);
        Assert.Equal("Monitor already running on port 6222.", result.Message);
    }

    [Fact]
    public async Task GetAgentStatusDetailedAsync_ReturnsStarting_WhenMonitorStartupIsInProgressAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 0,
            ProcessId = 9999,
            Errors = new List<string> { "Startup status: starting" },
        }).ConfigureAwait(false);

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: processId => Task.FromResult(processId == 9999));

        var service = this.CreateService();

        var result = await service.GetAgentStatusDetailedAsync().ConfigureAwait(false);

        Assert.False(result.IsRunning);
        Assert.Equal(5000, result.Port);
        Assert.Equal("monitor-starting", result.Error);
        Assert.Equal("Monitor is starting.", result.Message);
    }

    [Fact]
    public async Task StopAgentDetailedAsync_ReturnsAlreadyStopped_WhenMonitorInfoIsAbsentAsync()
    {
        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: Array.Empty<string>(),
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var service = this.CreateService();

        var result = await service.StopAgentDetailedAsync().ConfigureAwait(false);

        Assert.True(result.Success);
        Assert.Equal("Monitor already stopped (info file missing).", result.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDirectory))
        {
            Directory.Delete(this._tempDirectory, recursive: true);
        }
    }

    private MonitorProcessService CreateService()
    {
        return new MonitorProcessService(NullLogger<MonitorProcessService>.Instance);
    }

    private async Task<string> CreateMonitorInfoAsync(MonitorInfo info)
    {
        var path = Path.Combine(this._tempDirectory, "monitor.json");
        var json = JsonSerializer.Serialize(info);
        await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        return path;
    }
}
