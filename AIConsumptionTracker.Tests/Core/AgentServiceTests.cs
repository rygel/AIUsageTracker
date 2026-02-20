using AIConsumptionTracker.Core.AgentClient;
using AIConsumptionTracker.Core.Models;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIConsumptionTracker.Tests.Core;

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
