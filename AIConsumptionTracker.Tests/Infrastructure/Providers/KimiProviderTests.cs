using System.Net;
using System.Text.Json;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class KimiProviderTests
{
    private readonly Mock<HttpMessageHandler> _msgHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<KimiProvider>> _logger;
    private readonly KimiProvider _provider;

    public KimiProviderTests()
    {
        _msgHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_msgHandler.Object);
        _logger = new Mock<ILogger<KimiProvider>>();
        _provider = new KimiProvider(_httpClient, _logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_CalculatesPercentageCorrectly()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderId = "kimi",
            ApiKey = "test-key"
        };
        
        // 10 used, 100 limit, 90 remaining. Used% = 10%
        var responseContent = JsonSerializer.Serialize(new
        {
            usage = new { limit = 100, used = 10, remaining = 90 },
            limits = new object[] {} 
        });

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Get && 
                    req.RequestUri != null && req.RequestUri.AbsoluteUri == "https://api.kimi.com/coding/v1/usages"),
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
        Assert.Equal("Kimi", usage.ProviderName);
        Assert.Equal(10, usage.UsagePercentage);
        Assert.Equal(10, usage.CostUsed);
        Assert.Equal(100, usage.CostLimit);
        Assert.True(usage.IsQuotaBased);
    }

    [Fact]
    public async Task GetUsageAsync_WithLimitDetails_ParsesDetailsCorrectly()
    {
         // Arrange
        var config = new ProviderConfig { ProviderId = "kimi", ApiKey = "test-key" };

        var resetTime = DateTime.UtcNow.AddHours(1).ToString("o"); // ISO 8601
        
        var responseData = new
        {
            usage = new { limit = 100, used = 50, remaining = 50 },
            limits = new object[] 
            {
                new 
                {
                    window = new { duration = 60, timeUnit = "TIME_UNIT_MINUTE" },
                    detail = new { limit = 1000, remaining = 500, resetTime = resetTime }
                }
            }
        };

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(JsonSerializer.Serialize(responseData)) });

        // Act
        var result = await _provider.GetUsageAsync(config);
        
        // Assert
        var usage = result.Single();
        Assert.NotNull(usage.Details);
        Assert.Single(usage.Details);
        var detail = usage.Details.First();
        Assert.Equal("Hourly Limit", detail.Name);
        Assert.Contains((50.0).ToString("F1") + "%", detail.Used); // 500 remaining of 1000 limit -> 50% used
    }
}
