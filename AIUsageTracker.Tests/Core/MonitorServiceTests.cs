using AIUsageTracker.Core.AgentClient;
using AIUsageTracker.Core.Models;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIUsageTracker.Tests.Core;

public class AgentServiceTests
{
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly AgentService _service;

    public AgentServiceTests()
    {
        _mockHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:5000")
        };
        _service = new AgentService(_httpClient);
        _service.AgentUrl = "http://localhost:5000";
    }

    [Fact]
    public async Task CheckProviderAsync_Success_ReturnsOk()
    {
        // Arrange
        var responseObj = new { success = true, message = "Connected" };
        SetupMockResponse(HttpStatusCode.OK, responseObj);

        // Act
        var (success, message) = await _service.CheckProviderAsync("openai");

        // Assert
        Assert.True(success);
        Assert.Equal("Connected", message);
        VerifyPath("/api/providers/openai/check");
    }

    [Fact]
    public async Task CheckProviderAsync_Error_ReturnsFailure()
    {
        // Arrange
        var responseObj = new { success = false, message = "Invalid Key" };
        SetupMockResponse(HttpStatusCode.Unauthorized, responseObj);

        // Act
        var (success, message) = await _service.CheckProviderAsync("openai");

        // Assert
        Assert.False(success);
        Assert.Equal("Invalid Key", message);
    }

    [Fact]
    public async Task ExportDataAsync_Success_ReturnsStream()
    {
        // Arrange
        var expectedContent = "Time,Provider\n2026-02-19,openai";
        _mockHandler.Protected()
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
        var stream = await _service.ExportDataAsync("csv", 7);

        // Assert
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal(expectedContent, content);
        VerifyPath("/api/export", "format=csv", "days=7");
    }

    [Fact]
    public async Task GetUsageAsync_RecordsUsageTelemetry()
    {
        // Arrange
        var baseline = AgentService.GetTelemetrySnapshot();
        var usage = new List<ProviderUsage>
        {
            new() { ProviderId = "openai", ProviderName = "OpenAI", IsAvailable = true }
        };
        SetupMockResponse(HttpStatusCode.OK, usage);

        // Act
        var result = await _service.GetUsageAsync();
        var telemetry = AgentService.GetTelemetrySnapshot();

        // Assert
        Assert.Single(result);
        Assert.True(telemetry.UsageRequestCount >= baseline.UsageRequestCount + 1);
        Assert.True(telemetry.UsageErrorCount >= baseline.UsageErrorCount);
    }

    [Fact]
    public async Task TriggerRefreshAsync_RequestFails_RecordsRefreshErrorTelemetry()
    {
        // Arrange
        var baseline = AgentService.GetTelemetrySnapshot();
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network failure"));

        // Act
        var success = await _service.TriggerRefreshAsync();
        var telemetry = AgentService.GetTelemetrySnapshot();

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
            apiContractVersion = AgentService.ExpectedApiContractVersion,
            agentVersion = "2.1.3"
        };
        SetupMockResponse(HttpStatusCode.OK, responseObj);

        // Act
        var result = await _service.CheckApiContractAsync();

        // Assert
        Assert.True(result.IsReachable);
        Assert.True(result.IsCompatible);
        Assert.Equal(AgentService.ExpectedApiContractVersion, result.AgentContractVersion);
        VerifyPath("/api/health");
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
        SetupMockResponse(HttpStatusCode.OK, responseObj);

        // Act
        var result = await _service.CheckApiContractAsync();

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
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        var result = await _service.CheckApiContractAsync();

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
            apiContractVersion = AgentService.ExpectedApiContractVersion
        };
        SetupMockResponse(HttpStatusCode.OK, responseObj);

        // Act
        var result = await _service.GetHealthDetailsAsync();

        // Assert
        Assert.Contains("healthy", result, StringComparison.OrdinalIgnoreCase);
        VerifyPath("/api/health");
    }

    [Fact]
    public async Task GetDiagnosticsDetailsAsync_RequestFails_ReturnsErrorMessage()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network failure"));

        // Act
        var result = await _service.GetDiagnosticsDetailsAsync();

        // Assert
        Assert.Contains("Request failed", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTestNotificationDetailedAsync_Success_ReturnsSuccessMessage()
    {
        // Arrange
        SetupMockResponse(HttpStatusCode.OK, new { message = "ok" });

        // Act
        var result = await _service.SendTestNotificationDetailedAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Test sent", result.Message, StringComparison.OrdinalIgnoreCase);
        VerifyPath("/api/notifications/test");
    }

    [Fact]
    public async Task SendTestNotificationDetailedAsync_NotFound_ReturnsRestartHint()
    {
        // Arrange
        SetupMockResponse(HttpStatusCode.NotFound, new { message = "not found" });

        // Act
        var result = await _service.SendTestNotificationDetailedAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Restart Monitor", result.Message, StringComparison.OrdinalIgnoreCase);
        VerifyPath("/api/notifications/test");
    }

    [Fact]
    public async Task SendTestNotificationDetailedAsync_RequestFails_ReturnsUnreachableHint()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        var result = await _service.SendTestNotificationDetailedAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Ensure it is running", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private void SetupMockResponse(HttpStatusCode status, object body)
    {
        _mockHandler.Protected()
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

    private void VerifyPath(string path, params string[] queryParts)
    {
        _mockHandler.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.RequestUri != null && 
                req.RequestUri.AbsolutePath == path &&
                queryParts.All(qp => req.RequestUri.Query.Contains(qp))),
            ItExpr.IsAny<CancellationToken>());
    }
}

