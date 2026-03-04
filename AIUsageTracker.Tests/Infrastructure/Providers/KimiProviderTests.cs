using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

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
        
        // 10 used, 100 limit, 90 remaining. RequestsPercentage uses remaining semantics.
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
        Assert.Equal(90, usage.RequestsPercentage);
        Assert.Equal(10, usage.RequestsUsed);
        Assert.Equal(100, usage.RequestsAvailable);
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
        Assert.Contains("50.0%", detail.Used); // Hardcoded 50.0% to enforce InvariantCulture
    }

    [Fact]
    public async Task GetUsageAsync_WithHourlyAndWeeklyLimits_SetsCorrectWindowKinds()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "kimi", ApiKey = "test-key" };

        var hourlyResetTime = DateTime.UtcNow.AddMinutes(30).ToString("o");
        var weeklyResetTime = DateTime.UtcNow.AddDays(7).ToString("o");
        
        var responseData = new
        {
            usage = new { limit = 100000, used = 25000, remaining = 75000 },
            limits = new object[] 
            {
                new 
                {
                    window = new { duration = 60, timeUnit = "TIME_UNIT_MINUTE" },
                    detail = new { limit = 3000, remaining = 1800, resetTime = hourlyResetTime }
                },
                new 
                {
                    window = new { duration = 7, timeUnit = "TIME_UNIT_DAY" },
                    detail = new { limit = 100000, remaining = 75000, resetTime = weeklyResetTime }
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
        Assert.Equal(2, usage.Details.Count);
        
        var hourlyDetail = usage.Details.FirstOrDefault(d => d.WindowKind == WindowKind.Primary);
        var weeklyDetail = usage.Details.FirstOrDefault(d => d.WindowKind == WindowKind.Secondary);
        
        Assert.NotNull(hourlyDetail);
        Assert.NotNull(weeklyDetail);
        Assert.Equal("Hourly Limit", hourlyDetail.Name);
        Assert.Equal("7d Limit", weeklyDetail.Name);
    }
}

