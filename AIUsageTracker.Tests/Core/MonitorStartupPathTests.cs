// <copyright file="MonitorStartupPathTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Core;

[Collection("MonitorStartupPath")]
public sealed class MonitorStartupPathTests : IDisposable
{
    private readonly string _tempDirectory;

    public MonitorStartupPathTests()
    {
        this._tempDirectory = TestTempPaths.CreateDirectory("monitor-startup-tests");
    }

    [Fact]
    public async Task GetAndValidateMonitorInfoAsync_ReturnsMonitorInfo_WhenMetadataIsHealthyAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5123,
            ProcessId = 4242,
            Errors = new List<string> { "warning" },
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
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
    public async Task GetAndValidateMonitorInfoAsync_InvalidatesMonitorInfo_WhenMetadataIsStaleAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5124,
            ProcessId = 9999,
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var result = await MonitorLauncher.GetAndValidateMonitorInfoAsync();

        Assert.Null(result);
        Assert.False(File.Exists(infoPath));
        Assert.Single(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task GetAndValidateMonitorInfoAsync_InvalidatesMonitorInfo_WhenMetadataIsMalformedAsync()
    {
        var infoPath = await this.CreateMonitorInfoContentAsync("{ not valid json");

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath });

        var result = await MonitorLauncher.GetAndValidateMonitorInfoAsync();

        Assert.Null(result);
        Assert.False(File.Exists(infoPath));
        Assert.Single(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void GetReadCandidatePaths_ReturnsCanonicalPathOnly()
    {
        var appDataRoot = Path.Combine(this._tempDirectory, "appdata");
        var candidates = MonitorInfoPathCatalog.GetReadCandidatePaths(appDataRoot, this._tempDirectory);

        Assert.Collection(candidates, path => Assert.Equal(Path.Combine(appDataRoot, "AIUsageTracker", "monitor.json"), path));
    }

    [Fact]
    public async Task RefreshAgentInfoAsync_UsesValidMonitorInfoPortAndErrorsAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5333,
            ProcessId = 1111,
            Errors = new List<string> { "Startup status: running" },
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5333),
            processRunningAsync: processId => Task.FromResult(processId == 1111));

        var service = this.CreateMonitorService();

        await service.RefreshAgentInfoAsync();

        Assert.Equal("http://localhost:5333", service.AgentUrl);
        Assert.Single(service.LastAgentErrors);
        Assert.Contains("Startup status: running", service.LastAgentErrors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAgentInfoAsync_FallsBackToDefaultPortAndClearsErrors_WhenMetadataIsStaleAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5444,
            ProcessId = 2222,
            Errors = new List<string> { "stale" },
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var service = this.CreateMonitorService();
        service.AgentUrl = "http://localhost:5444";

        await service.RefreshAgentInfoAsync();

        Assert.Equal("http://localhost:5000", service.AgentUrl);
        Assert.Empty(service.LastAgentErrors);
        Assert.Single(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RefreshAgentInfoAsync_PreservesLaunchErrors_WhenMetadataShowsStartupFailureAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 0,
            ProcessId = 2222,
            Errors = new List<string> { "Startup status: failed: port bind failed" },
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var service = this.CreateMonitorService();
        service.AgentUrl = "http://localhost:5444";

        await service.RefreshAgentInfoAsync();

        Assert.Equal("http://localhost:5000", service.AgentUrl);
        var error = Assert.Single(service.LastAgentErrors);
        Assert.Contains("failed", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RefreshPortAsync_UsesHealthyResolvedPortAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5555,
            ProcessId = 3210,
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5555),
            processRunningAsync: processId => Task.FromResult(processId == 3210));

        var service = this.CreateMonitorService();
        service.AgentUrl = "http://localhost:5000";

        await service.RefreshPortAsync();

        Assert.Equal("http://localhost:5555", service.AgentUrl);
    }

    [Fact]
    public async Task RefreshPortAsync_KeepsExistingAgentUrl_WhenResolvedPortIsNotHealthyAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5444,
            ProcessId = 2222,
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var service = this.CreateMonitorService();
        service.AgentUrl = "http://localhost:5333";

        await service.RefreshPortAsync();

        Assert.Equal("http://localhost:5333", service.AgentUrl);
        Assert.Single(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task IsAgentRunningWithPortAsync_ReturnsHealthyMetadataPortAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5666,
            ProcessId = 3333,
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5666),
            processRunningAsync: processId => Task.FromResult(processId == 3333));

        var result = await MonitorLauncher.IsAgentRunningWithPortAsync();

        Assert.True(result.IsRunning);
        Assert.Equal(5666, result.Port);
    }

    [Fact]
    public async Task EnsureAgentRunningAsync_ReturnsTrue_WhenMonitorIsAlreadyHealthyAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5777,
            ProcessId = 4444,
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5777),
            processRunningAsync: processId => Task.FromResult(processId == 4444));

        var result = await MonitorLauncher.EnsureAgentRunningAsync();

        Assert.True(result);
        Assert.True(File.Exists(infoPath));
    }

    [Fact]
    public async Task WaitForAgentAsync_ReturnsFalse_WhenCancelledAsync()
    {
        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: Array.Empty<string>(),
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var result = await MonitorLauncher.WaitForAgentAsync(cancellationTokenSource.Token);

        Assert.False(result);
    }

    [Fact]
    public async Task WaitForAgentAsync_ReturnsFalseQuickly_WhenMetadataReportsStartupFailureAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 0,
            ProcessId = 7777,
            Errors = new List<string> { "Startup status: failed: port bind failed" },
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: _ => Task.FromResult(false));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await MonitorLauncher.WaitForAgentAsync(cancellationTokenSource.Token);

        stopwatch.Stop();
        Assert.False(result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "Startup failure should abort wait quickly.");
    }

    [Fact]
    public async Task GetAgentStatusInfoAsync_PreservesStartingMetadata_WhenProcessStillRunningAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 0,
            ProcessId = 7788,
            Errors = new List<string> { "Startup status: starting" },
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: _ => Task.FromResult(false),
            processRunningAsync: processId => Task.FromResult(processId == 7788));

        var result = await MonitorLauncher.GetAgentStatusInfoAsync();

        Assert.False(result.IsRunning);
        Assert.True(result.HasMetadata);
        Assert.Equal(5000, result.Port);
        Assert.Equal("monitor-starting", result.Error);
        Assert.Equal("Monitor is starting.", result.Message);
        Assert.True(File.Exists(infoPath));
        Assert.Empty(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task StopAgentAsync_InvalidatesMetadata_WhenKnownProcessStopsAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5888,
            ProcessId = 5555,
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5888),
            processRunningAsync: processId => Task.FromResult(processId == 5555),
            stopProcessAsync: processId => Task.FromResult(processId == 5555));

        var result = await MonitorLauncher.StopAgentAsync();

        Assert.True(result);
        Assert.False(File.Exists(infoPath));
        Assert.Single(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task StopAgentAsync_ReturnsFalse_WhenStopFailsAndHealthRemainsUpAsync()
    {
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = 5999,
            ProcessId = 6666,
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(port == 5999),
            processRunningAsync: processId => Task.FromResult(processId == 6666),
            stopProcessAsync: _ => Task.FromResult(false),
            stopNamedProcessesAsync: () => Task.FromResult(false));

        var result = await MonitorLauncher.StopAgentAsync();

        Assert.False(result);
        Assert.True(File.Exists(infoPath));
        Assert.Empty(Directory.GetFiles(this._tempDirectory, "monitor.json.stale.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RefreshPortAsync_WithLiveEndpoint_ReconnectsAfterStaleMetadataAndPortChangeAsync()
    {
        var firstProviderId = $"provider-a-{Guid.NewGuid():N}";
        var secondProviderId = $"provider-b-{Guid.NewGuid():N}";

        await using var firstEndpoint = await TestMonitorEndpoint.StartAsync(firstProviderId);
        var healthyPorts = new HashSet<int> { firstEndpoint.Port };
        var infoPath = await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = firstEndpoint.Port,
            ProcessId = 1234,
        });

        using var overrides = MonitorLauncher.PushTestOverrides(
            monitorInfoCandidatePaths: new[] { infoPath },
            healthCheckAsync: port => Task.FromResult(healthyPorts.Contains(port)),
            processRunningAsync: processId => Task.FromResult(processId == 1234 || processId == 5678));

        var service = new MonitorService(new HttpClient(), NullLogger<MonitorService>.Instance)
        {
            AgentUrl = "http://localhost:5000",
        };

        await service.RefreshPortAsync();
        Assert.Equal($"http://localhost:{firstEndpoint.Port}", service.AgentUrl);

        var firstUsage = await service.GetUsageAsync();
        Assert.Contains(firstUsage, usage => string.Equals(usage.ProviderId, firstProviderId, StringComparison.Ordinal));

        await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = this.GetUnusedPort(),
            ProcessId = 5678,
        });

        await service.RefreshPortAsync();
        Assert.Equal($"http://localhost:{firstEndpoint.Port}", service.AgentUrl);

        var preservedUsage = await service.GetUsageAsync();
        Assert.Contains(preservedUsage, usage => string.Equals(usage.ProviderId, firstProviderId, StringComparison.Ordinal));

        await using var secondEndpoint = await TestMonitorEndpoint.StartAsync(secondProviderId);
        healthyPorts.Add(secondEndpoint.Port);
        await this.CreateMonitorInfoAsync(new MonitorInfo
        {
            Port = secondEndpoint.Port,
            ProcessId = 5678,
        });

        await service.RefreshPortAsync();
        Assert.Equal($"http://localhost:{secondEndpoint.Port}", service.AgentUrl);

        var secondUsage = await service.GetUsageAsync();
        Assert.Contains(secondUsage, usage => string.Equals(usage.ProviderId, secondProviderId, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._tempDirectory);
    }

    private MonitorService CreateMonitorService()
    {
        return new MonitorService(new HttpClient(new Mock<HttpMessageHandler>().Object), NullLogger<MonitorService>.Instance);
    }

    private async Task<string> CreateMonitorInfoAsync(MonitorInfo info)
    {
        var json = JsonSerializer.Serialize(info);
        return await this.CreateMonitorInfoContentAsync(json).ConfigureAwait(false);
    }

    private async Task<string> CreateMonitorInfoContentAsync(string content)
    {
        var path = Path.Combine(this._tempDirectory, "monitor.json");
        await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
        return path;
    }

    private int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.IPv6Any, 0);
        listener.Server.DualMode = true;
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class TestMonitorEndpoint : IAsyncDisposable
    {
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Task _acceptLoopTask;
        private readonly string _providerId;

        private TestMonitorEndpoint(TcpListener listener, string providerId)
        {
            this._listener = listener;
            this._providerId = providerId;
            this.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            this._acceptLoopTask = this.AcceptLoopAsync();
        }

        public int Port { get; }

        public static Task<TestMonitorEndpoint> StartAsync(string providerId)
        {
            var listener = new TcpListener(IPAddress.IPv6Any, 0);
            listener.Server.DualMode = true;
            listener.Start();
            return Task.FromResult(new TestMonitorEndpoint(listener, providerId));
        }

        public async ValueTask DisposeAsync()
        {
            await this._cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            this._listener.Stop();

            try
            {
#pragma warning disable VSTHRD003 // Awaiting a fixture-owned task in cleanup is intentional.
                await this._acceptLoopTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!this._cancellationTokenSource.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await this._listener.AcceptTcpClientAsync(this._cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                _ = this.HandleClientAsync(client);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using var clientHandle = client;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            while (!string.IsNullOrEmpty(await reader.ReadLineAsync().ConfigureAwait(false)))
            {
            }

            var requestPath = requestLine.Split(' ')[1];
            var payload = requestPath switch
            {
                "/api/health" => JsonSerializer.Serialize(new
                {
                    status = "healthy",
                    contract_version = MonitorService.ExpectedApiContractVersion,
                    api_contract_version = MonitorService.ExpectedApiContractVersion,
                    min_client_contract_version = MonitorService.ExpectedApiContractVersion,
                    min_client_api_contract_version = MonitorService.ExpectedApiContractVersion,
                    agent_version = "test-endpoint",
                }),
                "/api/usage" => JsonSerializer.Serialize(
                    new[]
                    {
                        new ProviderUsage
                        {
                            ProviderId = this._providerId,
                            ProviderName = this._providerId,
                            IsAvailable = true,
                        },
                    },
                    this._jsonOptions),
                _ => JsonSerializer.Serialize(new { message = "not found" }),
            };

            var statusLine = requestPath is "/api/health" or "/api/usage"
                ? "HTTP/1.1 200 OK"
                : "HTTP/1.1 404 Not Found";
            var body = Encoding.UTF8.GetBytes(payload);
            var header = Encoding.ASCII.GetBytes(
                $"{statusLine}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");

            await stream.WriteAsync(header).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
    }
}
