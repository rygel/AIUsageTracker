// <copyright file="MonitorServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Core
{
    using AIUsageTracker.Core.MonitorClient;
    using AIUsageTracker.Core.Models;
    using Microsoft.Extensions.Logging.Abstractions;
    using Moq;
    using Moq.Protected;
    using System.Net;
    using System.Net.Http.Json;
    using System.Text.Json;

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
                BaseAddress = new Uri("http://localhost:5000")
            };
            this._service = new MonitorService(this._httpClient, NullLogger<MonitorService>.Instance);
            this._service.AgentUrl = "http://localhost:5000";
        }

        [Fact]
        public async Task CheckProviderAsync_Success_ReturnsOk()
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
        public async Task CheckProviderAsync_Error_ReturnsFailure()
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
        public async Task ExportDataAsync_Success_ReturnsStream()
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
                    Content = new StringContent(expectedContent)
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
        public async Task GetUsageAsync_RecordsUsageTelemetry()
        {
            // Arrange
            var baseline = MonitorService.GetTelemetrySnapshot();
            var usage = new List<ProviderUsage>
            {
                new() { ProviderId = "openai", ProviderName = "OpenAI", IsAvailable = true }
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
        public async Task TriggerRefreshAsync_RequestFails_RecordsRefreshErrorTelemetry()
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
        public async Task CheckApiContractAsync_Compatible_ReturnsCompatibleResult()
        {
            // Arrange
            var responseObj = new
            {
                status = "healthy",
                apiContractVersion = MonitorService.ExpectedApiContractVersion,
                agentVersion = "2.1.3"
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
        public async Task CheckApiContractAsync_Mismatch_ReturnsWarningResult()
        {
            // Arrange
            var responseObj = new
            {
                status = "healthy",
                apiContractVersion = "999",
                agentVersion = "2.1.3"
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
        public async Task CheckApiContractAsync_RequestFails_ReturnsUnreachableResult()
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
        public async Task GetHealthDetailsAsync_Success_ReturnsPayload()
        {
            // Arrange
            var responseObj = new
            {
                status = "healthy",
                apiContractVersion = MonitorService.ExpectedApiContractVersion
            };
            this.SetupMockResponse(HttpStatusCode.OK, responseObj);

            // Act
            var result = await this._service.GetHealthDetailsAsync();

            // Assert
            Assert.Contains("healthy", result, StringComparison.OrdinalIgnoreCase);
            this.VerifyPath("/api/health");
        }

        [Fact]
        public async Task GetDiagnosticsDetailsAsync_RequestFails_ReturnsErrorMessage()
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
        public async Task SendTestNotificationDetailedAsync_Success_ReturnsSuccessMessage()
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
        public async Task SendTestNotificationDetailedAsync_NotFound_ReturnsRestartHint()
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
        public async Task SendTestNotificationDetailedAsync_RequestFails_ReturnsUnreachableHint()
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
        public async Task GetUsageByProviderAsync_RequestFails_ReturnsNull()
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
        public async Task ScanForKeysAsync_Success_ReturnsDiscoveredConfigs()
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
                    }
                });

            // Act
            var result = await this._service.ScanForKeysAsync();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal(2, result.Configs.Count);
            this.VerifyPath("/api/scan-keys");
        }

        [Fact]
        public async Task ScanForKeysAsync_InvalidJson_ReturnsEmptyResult()
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
    `n
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
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    })
                });
        }
    `n
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
    }

}
