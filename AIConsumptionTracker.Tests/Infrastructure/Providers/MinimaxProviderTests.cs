using System.Net;
using System.Text.Json;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class MinimaxProviderTests
{
    private readonly Mock<HttpMessageHandler> _msgHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<MinimaxProvider>> _logger;
    private readonly MinimaxProvider _provider;

    public MinimaxProviderTests()
    {
        _msgHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_msgHandler.Object);
        _logger = new Mock<ILogger<MinimaxProvider>>();
        _provider = new MinimaxProvider(_httpClient, _logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_MinimaxChat_UsesCorrectUrl()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderId = "minimax",
            ApiKey = "test-key"
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            usage = new { tokens_used = 100, tokens_limit = 1000 },
            base_resp = new { status_code = 0 }
        });

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Get && 
                    req.RequestUri != null && req.RequestUri.AbsoluteUri == "https://api.minimax.chat/v1/user/usage"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert
        var usage = result.Single();
        Assert.Equal("Minimax", usage.ProviderName);
        Assert.Equal(100, usage.CostUsed);
        Assert.Equal(1000, usage.CostLimit);
    }

    [Fact]
    public async Task GetUsageAsync_MinimaxIo_UsesCorrectUrl()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderId = "minimax-io",
            ApiKey = "test-key"
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            usage = new { tokens_used = 50, tokens_limit = 500 },
            base_resp = new { status_code = 0 }
        });

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Get && 
                    req.RequestUri != null && req.RequestUri.AbsoluteUri == "https://api.minimax.io/v1/user/usage"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert
        var usage = result.Single();
        Assert.Equal("Minimax", usage.ProviderName);
        Assert.Equal(50, usage.CostUsed);
        Assert.Equal(500, usage.CostLimit);
    }

    [Fact]
    public async Task GetUsageAsync_InvalidApiKey_ThrowsException()
    {
         // Arrange
        var config = new ProviderConfig
        {
            ProviderId = "minimax",
            ApiKey = "" // Empty key
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _provider.GetUsageAsync(config));
    }

    [Fact]
    public async Task GetUsageAsync_ApiError_ThrowsException()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderId = "minimax",
            ApiKey = "test-key"
        };

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized, // 401
                Content = new StringContent("Unauthorized")
            });

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _provider.GetUsageAsync(config));
    }
}
