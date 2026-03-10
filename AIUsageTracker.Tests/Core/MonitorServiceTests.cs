// <copyright file="MonitorServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Core;

public class MonitorServiceTests
{
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly MonitorService _service;

    public MonitorServiceTests()
    {
        this._mockHandler = new Mock<HttpMessageHandler>();
        this._httpClient = new HttpClient(this._mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:5000"),
        };
        this._service = new MonitorService(this._httpClient, NullLogger<MonitorService>.Instance);
        this._service.AgentUrl = "http://localhost:5000";
    }

    [Fact]
    public async Task CheckProviderAsync_Success_ReturnsOkAsync()
    {
        // Arrange
        var responseObj = new { success = true, message = "Connected" };
        this.SetupMockResponse(HttpStatusCode.OK, responseObj);

        // Act
        var (success, message) = await this._service.CheckProviderAsync("openai");

        // Assert
        Assert.True(success);
        Assert.Equal("Connected", message);
        this.VerifyPath("/api/providers/openai/check");
    }

    [Fact]
    public async Task CheckProviderAsync_Error_ReturnsFailureAsync()
    {
        // Arrange
        var responseObj = new { success = false, message = "Invalid Key" };
        this.SetupMockResponse(HttpStatusCode.Unauthorized, responseObj);

        // Act
        var (success, message) = await this._service.CheckProviderAsync("openai");

        // Assert
        Assert.False(success);
        Assert.Equal("Invalid Key", message);
    }

    [Fact]
    public async Task ExportDataAsync_Success_ReturnsStreamAsync()
    {
        // Arrange
        var expectedContent = "Time,Provider\n2026-02-19,openai";
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(expectedContent),
            });

        // Act
        var stream = await this._service.ExportDataAsync("csv", 7);

        // Assert
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal(expectedContent, content);
        this.VerifyPath("/api/export", "format=csv", "days=7");
    }

    [Fact]
    public async Task GetUsageAsync_RecordsUsageTelemetryAsync()
    {
        // Arrange
        var baseline = MonitorService.GetTelemetrySnapshot();
        var usage = new List<ProviderUsage>
        {
            new() { ProviderId = "openai", ProviderName = "OpenAI", IsAvailable = true },
        };
        this.SetupMockResponse(HttpStatusCode.OK, usage);

        // Act
        var result = await this._service.GetUsageAsync();
        var telemetry = MonitorService.GetTelemetrySnapshot();

        // Assert
        Assert.Single(result);
        Assert.True(telemetry.UsageRequestCount >= baseline.UsageRequestCount + 1);
        Assert.True(telemetry.UsageErrorCount >= baseline.UsageErrorCount);
    }

    [Fact]
    public async Task GetUsageAsync_RevalidatesEndpointBeforeRequestAsync()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "monitor-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var infoPath = await this.CreateMonitorInfoAsync(tempDirectory, new MonitorInfo
            {
                Port = 5333,
                ProcessId = 4242,
            });

            using var overrides = MonitorLauncher.PushTestOverrides(
                monitorInfoCandidatePaths: new[] { infoPath },
                healthCheckAsync: port => Task.FromResult(port == 5333),
                processRunningAsync: processId => Task.FromResult(processId == 4242));

            var requestedUrls = new List<string>();
            var usage = new List<ProviderUsage>
            {
                new() { ProviderId = "openai", ProviderName = "OpenAI", IsAvailable = true },
            };

            this._service.AgentUrl = "http://localhost:5000";

            this._mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
                {
                    requestedUrls.Add(request.RequestUri!.ToString());
                    return Task.FromResult(new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = JsonContent.Create(usage, options: new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        }),
                    });
                });

            var result = await this._service.GetUsageAsync();

            Assert.Single(result);
            Assert.Equal("http://localhost:5333", this._service.AgentUrl);
            Assert.Single(requestedUrls);
            Assert.Equal("http://localhost:5333/api/usage", requestedUrls[0]);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GetUsageAsync_RequestTimesOut_RefreshesEndpointAndRetriesAsync()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "monitor-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var infoPath = await this.CreateMonitorInfoAsync(tempDirectory, new MonitorInfo
            {
                Port = 5333,
                ProcessId = 4242,
            });

            using var overrides = MonitorLauncher.PushTestOverrides(
                monitorInfoCandidatePaths: new[] { infoPath },
                healthCheckAsync: port => Task.FromResult(port == 5333),
                processRunningAsync: processId => Task.FromResult(processId == 4242));

            var requestedUrls = new List<string>();
            var usage = new List<ProviderUsage>
            {
                new() { ProviderId = "openai", ProviderName = "OpenAI", IsAvailable = true },
            };

            this._service.AgentUrl = "http://localhost:5000";

            this._mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
                {
                    requestedUrls.Add(request.RequestUri!.ToString());
                    return requestedUrls.Count == 1
                        ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"))
                        : Task.FromResult(new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK,
                            Content = JsonContent.Create(usage, options: new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                            }),
                        });
                });

            var result = await this._service.GetUsageAsync();

            Assert.Single(result);
            Assert.Equal("http://localhost:5333", this._service.AgentUrl);
            Assert.Equal(2, requestedUrls.Count);
            Assert.All(requestedUrls, requestedUrl => Assert.Equal("http://localhost:5333/api/usage", requestedUrl));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GetUsageAsync_RequestTimesOutTwice_ReturnsEmptyListAsync()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "monitor-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var infoPath = await this.CreateMonitorInfoAsync(tempDirectory, new MonitorInfo
            {
                Port = 5333,
                ProcessId = 4242,
            });

            using var overrides = MonitorLauncher.PushTestOverrides(
                monitorInfoCandidatePaths: new[] { infoPath },
                healthCheckAsync: port => Task.FromResult(port == 5333),
                processRunningAsync: processId => Task.FromResult(processId == 4242));

            this._service.AgentUrl = "http://localhost:5000";

            this._mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new TaskCanceledException("timeout"));

            var result = await this._service.GetUsageAsync();

            Assert.Empty(result);
            Assert.Equal("http://localhost:5333", this._service.AgentUrl);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task TriggerRefreshAsync_RevalidatesEndpointBeforeRefreshRequestAsync()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "monitor-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var infoPath = await this.CreateMonitorInfoAsync(tempDirectory, new MonitorInfo
            {
                Port = 5444,
                ProcessId = 5151,
            });

            using var overrides = MonitorLauncher.PushTestOverrides(
                monitorInfoCandidatePaths: new[] { infoPath },
                healthCheckAsync: port => Task.FromResult(port == 5444),
                processRunningAsync: processId => Task.FromResult(processId == 5151));

            this._service.AgentUrl = "http://localhost:5000";
            this.SetupMockResponse(HttpStatusCode.OK, new { success = true });

            var success = await this._service.TriggerRefreshAsync();

            Assert.True(success);
            Assert.Equal("http://localhost:5444", this._service.AgentUrl);
            this._mockHandler.Protected().Verify(
                "SendAsync",
                Times.AtLeastOnce(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.RequestUri.ToString() == "http://localhost:5444/api/refresh" &&
                    req.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task TriggerRefreshAsync_RequestFails_RecordsRefreshErrorTelemetryAsync()
    {
        // Arrange
        var baseline = MonitorService.GetTelemetrySnapshot();
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network failure"));

        // Act
        var success = await this._service.TriggerRefreshAsync();
        var telemetry = MonitorService.GetTelemetrySnapshot();

        // Assert
        Assert.False(success);
        Assert.True(telemetry.RefreshRequestCount >= baseline.RefreshRequestCount + 1);
        Assert.True(telemetry.RefreshErrorCount >= baseline.RefreshErrorCount + 1);
    }

    [Fact]
    public async Task CheckHealthAsync_RevalidatesEndpointBeforeHealthRequestAsync()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "monitor-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var infoPath = await this.CreateMonitorInfoAsync(tempDirectory, new MonitorInfo
            {
                Port = 5666,
                ProcessId = 6767,
            });

            using var overrides = MonitorLauncher.PushTestOverrides(
                monitorInfoCandidatePaths: new[] { infoPath },
                healthCheckAsync: port => Task.FromResult(port == 5666),
                processRunningAsync: processId => Task.FromResult(processId == 6767));

            this._service.AgentUrl = "http://localhost:5000";
            this.SetupMockResponse(HttpStatusCode.OK, new
            {
                status = "healthy",
                apiContractVersion = MonitorService.ExpectedApiContractVersion,
            });

            var isHealthy = await this._service.CheckHealthAsync();

            Assert.True(isHealthy);
            Assert.Equal("http://localhost:5666", this._service.AgentUrl);
            this._mockHandler.Protected().Verify(
                "SendAsync",
                Times.AtLeastOnce(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                    req.RequestUri.ToString() == "http://localhost:5666/api/health" &&
                    req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CheckApiContractAsync_Compatible_ReturnsCompatibleResultAsync()
    {
        // Arrange
        var responseObj = new
        {
            status = "healthy",
            apiContractVersion = MonitorService.ExpectedApiContractVersion,
            agentVersion = "2.1.3",
        };
        this.SetupMockResponse(HttpStatusCode.OK, responseObj);

        // Act
        var result = await this._service.CheckApiContractAsync();

        // Assert
        Assert.True(result.IsReachable);
        Assert.True(result.IsCompatible);
        Assert.Equal(MonitorService.ExpectedApiContractVersion, result.AgentContractVersion);
        this.VerifyPath("/api/health");
    }

    [Fact]
    public async Task CheckApiContractAsync_Mismatch_ReturnsWarningResultAsync()
    {
        // Arrange
        var responseObj = new
        {
            status = "healthy",
            apiContractVersion = "999",
            agentVersion = "2.1.3",
        };
        this.SetupMockResponse(HttpStatusCode.OK, responseObj);

        // Act
        var result = await this._service.CheckApiContractAsync();

        // Assert
        Assert.True(result.IsReachable);
        Assert.False(result.IsCompatible);
        Assert.Equal("999", result.AgentContractVersion);
        Assert.True(result.Message.Contains("mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CheckApiContractAsync_SnakeCaseContractFields_ReturnsCompatibleResultAsync()
    {
        var responseObj = new
        {
            status = "healthy",
            api_contract_version = MonitorService.ExpectedApiContractVersion,
            agent_version = "3.0.0",
        };
        this.SetupMockResponse(HttpStatusCode.OK, responseObj);

        var result = await this._service.CheckApiContractAsync();

        Assert.True(result.IsReachable);
        Assert.True(result.IsCompatible);
        Assert.Equal(MonitorService.ExpectedApiContractVersion, result.AgentContractVersion);
        Assert.Equal("3.0.0", result.AgentVersion);
    }

    [Fact]
    public async Task CheckApiContractAsync_LegacyVersionField_UsesVersionFallbackAsync()
    {
        var responseObj = new
        {
            status = "healthy",
            apiContractVersion = "999",
            version = "2.9.1",
        };
        this.SetupMockResponse(HttpStatusCode.OK, responseObj);

        var result = await this._service.CheckApiContractAsync();

        Assert.True(result.IsReachable);
        Assert.False(result.IsCompatible);
        Assert.Equal("999", result.AgentContractVersion);
        Assert.Equal("2.9.1", result.AgentVersion);
    }

    [Fact]
    public async Task CheckApiContractAsync_RequestFails_ReturnsUnreachableResultAsync()
    {
        // Arrange
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        var result = await this._service.CheckApiContractAsync();

        // Assert
        Assert.False(result.IsReachable);
        Assert.False(result.IsCompatible);
        Assert.True(result.Message.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_Success_ReturnsTypedHealthAsync()
    {
        this.SetupMockResponse(HttpStatusCode.OK, new
        {
            status = "healthy",
            timestamp = "2026-03-10T12:00:00Z",
            port = 5000,
            process_id = 100,
            api_contract_version = MonitorService.ExpectedApiContractVersion,
            agent_version = "3.1.0",
        });

        var result = await this._service.GetHealthSnapshotAsync();

        Assert.NotNull(result);
        Assert.Equal("healthy", result.Status);
        Assert.Equal(5000, result.Port);
        Assert.Equal(100, result.ProcessId);
        Assert.Equal(MonitorService.ExpectedApiContractVersion, result.ResolveApiContractVersion());
        Assert.Equal("3.1.0", result.ResolveAgentVersion());
        this.VerifyPath("/api/health");
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_RequestFails_ReturnsNullAsync()
    {
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await this._service.GetHealthSnapshotAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHealthDetailsAsync_Success_ReturnsPayloadAsync()
    {
        // Arrange
        var responseObj = new
        {
            status = "healthy",
            apiContractVersion = MonitorService.ExpectedApiContractVersion,
        };
        this.SetupMockResponse(HttpStatusCode.OK, responseObj);

        // Act
        var result = await this._service.GetHealthDetailsAsync();

        // Assert
        Assert.Contains("healthy", result, StringComparison.OrdinalIgnoreCase);
        this.VerifyPath("/api/health");
    }

    [Fact]
    public async Task GetDiagnosticsDetailsAsync_RequestFails_ReturnsErrorMessageAsync()
    {
        // Arrange
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network failure"));

        // Act
        var result = await this._service.GetDiagnosticsDetailsAsync();

        // Assert
        Assert.Contains("Request failed", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDiagnosticsSnapshotAsync_Success_ReturnsTypedTelemetryAsync()
    {
        this.SetupMockResponse(HttpStatusCode.OK, CreateDiagnosticsPayload());

        var diagnostics = await this._service.GetDiagnosticsSnapshotAsync();

        Assert.NotNull(diagnostics);
        Assert.Equal(5003, diagnostics.Port);
        Assert.Equal(1234, diagnostics.ProcessId);
        Assert.Single(diagnostics.Endpoints);
        Assert.Equal("/api/usage", diagnostics.Endpoints[0].Route);
        Assert.Equal(5, diagnostics.RefreshTelemetry?.RefreshCount);
        Assert.Equal(45, diagnostics.SchedulerTelemetry?.EnqueuedJobs);
        Assert.Equal(3, diagnostics.PipelineTelemetry?.PlaceholderFilteredCount);
        Assert.Equal(2, diagnostics.Observability?.ActivitySourceNames.Count);
        Assert.Equal("AIUsageTracker.Monitor.Refresh", diagnostics.Observability?.ActivitySourceNames[0]);
        this.VerifyPath("/api/diagnostics");
    }

    [Fact]
    public async Task GetDiagnosticsSnapshotAsync_RequestFails_ReturnsNullAsync()
    {
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network failure"));

        var diagnostics = await this._service.GetDiagnosticsSnapshotAsync();

        Assert.Null(diagnostics);
    }

    [Fact]
    public async Task GetDiagnosticsSnapshotAsync_InterfaceContract_ReturnsTypedTelemetryAsync()
    {
        var monitorService = new Mock<IMonitorService>();
        monitorService
            .Setup(service => service.GetDiagnosticsSnapshotAsync())
            .ReturnsAsync(new AgentDiagnosticsSnapshot
            {
                Port = 5099,
                ProcessId = 4321,
                Runtime = ".NET 8",
                Observability = new AgentObservabilitySnapshot
                {
                    ActivitySourceNames = ["A", "B"],
                },
                SchedulerTelemetry = new AgentSchedulerTelemetrySnapshot
                {
                    TotalQueuedJobs = 7,
                    EnqueuedJobs = 15,
                },
            });

        var diagnostics = await monitorService.Object.GetDiagnosticsSnapshotAsync();

        Assert.NotNull(diagnostics);
        Assert.Equal(5099, diagnostics.Port);
        Assert.Equal(4321, diagnostics.ProcessId);
        Assert.Equal(".NET 8", diagnostics.Runtime);
        Assert.Equal(["A", "B"], diagnostics.Observability?.ActivitySourceNames);
        Assert.Equal(7, diagnostics.SchedulerTelemetry?.TotalQueuedJobs);
        Assert.Equal(15, diagnostics.SchedulerTelemetry?.EnqueuedJobs);
    }

    [Fact]
    public async Task GetDiagnosticsSnapshotAsync_InterfaceContract_NullPayloadReturnsNullAsync()
    {
        var monitorService = new Mock<IMonitorService>();
        monitorService
            .Setup(service => service.GetDiagnosticsSnapshotAsync())
            .ReturnsAsync((AgentDiagnosticsSnapshot?)null);

        var diagnostics = await monitorService.Object.GetDiagnosticsSnapshotAsync();

        Assert.Null(diagnostics);
    }

    [Fact]
    public async Task SendTestNotificationDetailedAsync_Success_ReturnsSuccessMessageAsync()
    {
        // Arrange
        this.SetupMockResponse(HttpStatusCode.OK, new { success = true, message = "Test sent" });

        // Act
        var result = await this._service.SendTestNotificationDetailedAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Test sent", result.Message, StringComparison.OrdinalIgnoreCase);
        this.VerifyPath("/api/notifications/test");
    }

    [Fact]
    public async Task SendTestNotificationDetailedAsync_NotFound_ReturnsRestartHintAsync()
    {
        // Arrange
        this.SetupMockResponse(HttpStatusCode.NotFound, new { message = "not found" });

        // Act
        var result = await this._service.SendTestNotificationDetailedAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Restart Monitor", result.Message, StringComparison.OrdinalIgnoreCase);
        this.VerifyPath("/api/notifications/test");
    }

    [Fact]
    public async Task SendTestNotificationDetailedAsync_RequestFails_ReturnsUnreachableHintAsync()
    {
        // Arrange
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        var result = await this._service.SendTestNotificationDetailedAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Ensure it is running", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageByProviderAsync_RequestFails_ReturnsNullAsync()
    {
        // Arrange
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        var result = await this._service.GetUsageByProviderAsync("openai");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ScanForKeysAsync_Success_ReturnsDiscoveredConfigsAsync()
    {
        // Arrange
        this.SetupMockResponse(
            HttpStatusCode.OK,
            new
            {
                discovered = 2,
                configs = new[]
                {
                    new { providerId = "openai", providerName = "OpenAI" },
                    new { providerId = "anthropic", providerName = "Anthropic" },
                },
            });

        // Act
        var result = await this._service.ScanForKeysAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Configs.Count);
        this.VerifyPath("/api/scan-keys");
    }

    [Fact]
    public async Task ScanForKeysAsync_InvalidJson_ReturnsEmptyResultAsync()
    {
        // Arrange
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{not-valid-json}", System.Text.Encoding.UTF8, "application/json"),
            });

        // Act
        var result = await this._service.ScanForKeysAsync();

        // Assert
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Configs);
        this.VerifyPath("/api/scan-keys");
    }

    private void SetupMockResponse(HttpStatusCode status, object body)
    {
        this._mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = JsonContent.Create(body, options: new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                }),
            });
    }

    private void VerifyPath(string path, params string[] queryParts)
    {
        this._mockHandler.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri != null &&
                req.RequestUri.AbsolutePath == path &&
                queryParts.All(qp => req.RequestUri.Query.Contains(qp))),
            ItExpr.IsAny<CancellationToken>());
    }

    private async Task<string> CreateMonitorInfoAsync(string directory, MonitorInfo info)
    {
        var path = Path.Combine(directory, "monitor.json");
        var json = JsonSerializer.Serialize(info);
        await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        return path;
    }

    private static object CreateDiagnosticsPayload()
    {
        return new
        {
            port = 5003,
            process_id = 1234,
            working_dir = "C:/monitor",
            base_dir = "C:/monitor/bin",
            started_at = "2026-03-10 11:00:00",
            os = "Windows",
            runtime = ".NET 8",
            args = new[] { "--debug" },
            endpoints = new[]
            {
                new
                {
                    route = "/api/usage",
                    methods = new[] { "GET" },
                },
            },
            refresh_telemetry = new
            {
                refresh_count = 5,
                refresh_success_count = 4,
                refresh_failure_count = 1,
                error_rate_percent = 20.0,
                average_latency_ms = 15.2,
                last_latency_ms = 10,
            },
            scheduler_telemetry = new
            {
                high_priority_queued_jobs = 1,
                normal_priority_queued_jobs = 2,
                low_priority_queued_jobs = 0,
                total_queued_jobs = 3,
                recurring_jobs = 1,
                executed_jobs = 40,
                failed_jobs = 2,
                enqueued_jobs = 45,
                dequeued_jobs = 44,
                coalesced_skipped_jobs = 3,
                dispatch_noop_signals = 1,
                in_flight_jobs = 0,
            },
            pipeline_telemetry = new
            {
                total_processed_entries = 100,
                total_accepted_entries = 90,
                total_rejected_entries = 10,
                invalid_identity_count = 1,
                inactive_provider_filtered_count = 2,
                placeholder_filtered_count = 3,
                detail_contract_adjusted_count = 4,
                normalized_count = 20,
                privacy_redacted_count = 8,
                last_run_total_entries = 6,
                last_run_accepted_entries = 5,
            },
            observability = new
            {
                activity_source_names = new[]
                {
                    "AIUsageTracker.Monitor.Refresh",
                    "AIUsageTracker.Monitor.Scheduler",
                },
            },
        };
    }
}
