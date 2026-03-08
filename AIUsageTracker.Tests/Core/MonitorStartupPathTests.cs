using System.Text.Json;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Core;

[Collection("MonitorStartupPath")]
public sealed class MonitorStartupPathTests : IDisposable
{
    private readonly string _tempDirectory;

    public MonitorStartupPathTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "monitor-startup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task GetAndValidateMonitorInfoAsync_ReturnsMonitorInfo_WhenMetadataIsHealthy()
    {
        var infoPath = await CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5123,
            ProcessId = 4242,
            Errors = new List<string> { "warning" }
        });

        using var _ = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5123),
            processRunningAsync: processId => Task.FromResult(processId == 4242));

        var result = await MonitorLauncher.GetAndValidateMonitorInfoAsync();

        Assert.NotNull(result);
        Assert.Equal(5123, result!.Port);
        Assert.Equal(4242, result.ProcessId);
        Assert.True(File.Exists(infoPath));
    }

    [Fact]
    public async Task GetAndValidateMonitorInfoAsync_InvalidatesMonitorInfo_WhenMetadataIsStale()
    {
        var infoPath = await CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5124,
            ProcessId = 9999
        });

        using var _ = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var result = await MonitorLauncher.GetAndValidateMonitorInfoAsync();

        Assert.Null(result);
        Assert.False(File.Exists(infoPath));
        Assert.Single(Directory.GetFiles(_tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RefreshAgentInfoAsync_UsesValidMonitorInfoPortAndErrors()
    {
        var infoPath = await CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5333,
            ProcessId = 1111,
            Errors = new List<string> { "Startup status: running" }
        });

        using var _ = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5333),
            processRunningAsync: processId => Task.FromResult(processId == 1111));

        var service = CreateMonitorService();

        await service.RefreshAgentInfoAsync();

        Assert.Equal("http://localhost:5333", service.AgentUrl);
        Assert.Single(service.LastAgentErrors);
        Assert.Contains("Startup status: running", service.LastAgentErrors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAgentInfoAsync_FallsBackToDefaultPortAndClearsErrors_WhenMetadataIsStale()
    {
        var infoPath = await CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5444,
            ProcessId = 2222,
            Errors = new List<string> { "stale" }
        });

        using var _ = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var service = CreateMonitorService();
        service.AgentUrl = "http://localhost:5444";

        await service.RefreshAgentInfoAsync();

        Assert.Equal("http://localhost:5000", service.AgentUrl);
        Assert.Empty(service.LastAgentErrors);
        Assert.Single(Directory.GetFiles(_tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task IsAgentRunningWithPortAsync_ReturnsHealthyMetadataPort()
    {
        var infoPath = await CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5666,
            ProcessId = 3333,
        });

        using var _ = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5666),
            processRunningAsync: processId => Task.FromResult(processId == 3333));

        var result = await MonitorLauncher.IsAgentRunningWithPortAsync();

        Assert.True(result.IsRunning);
        Assert.Equal(5666, result.Port);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private MonitorService CreateMonitorService()
    {
        return new MonitorService(new HttpClient(new Mock<HttpMessageHandler>().Object), NullLogger<MonitorService>.Instance);
    }

    private async Task<string> CreateMonitorInfoAsync(MonitorInfo info)
    {
        var path = Path.Combine(_tempDirectory, "monitor.json");
        var json = JsonSerializer.Serialize(info);
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}
