using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class DeepSeekProviderTests
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<DeepSeekProvider>> _loggerMock;
    private readonly DeepSeekProvider _provider;

    public DeepSeekProviderTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object);
        _loggerMock = new Mock<ILogger<DeepSeekProvider>>();
        _provider = new DeepSeekProvider(_httpClient, _loggerMock.Object);
    }

    [Fact]
    public async Task GetUsageAsync_ShouldParseMultiCurrencyBalanceCorrectly()
    {
        // Arrange
        var config = new ProviderConfig { ApiKey = "test-key" };
        var responseJson = @"{
            ""is_available"": true,
            ""balance_infos"": [
                {
                    ""currency"": ""CNY"",
                    ""total_balance"": 150.50,
                    ""granted_balance"": 50.00,
                    ""topped_up_balance"": 100.50
                },
                {
                    ""currency"": ""USD"",
                    ""total_balance"": 10.00,
                    ""granted_balance"": 0.00,
                    ""topped_up_balance"": 10.00
                }
            ]
        }";

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null && req.RequestUri.AbsoluteUri == "https://api.deepseek.com/user/balance"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        // Act
        var result = await _provider.GetUsageAsync(config);
        var usages = result.ToList();

        // Assert
        Assert.Single(usages);
        var usage = usages[0];
        Assert.True(usage.IsAvailable);
        Assert.Equal("Balance: ¥150.50", usage.Description);
        Assert.Equal(2, usage.Details?.Count);
        
        var cnyDetail = usage.Details?.FirstOrDefault(d => d.Name == "Balance (CNY)");
        Assert.NotNull(cnyDetail);
        Assert.Equal("¥150.50", cnyDetail.Used);
        Assert.Contains("Topped-up: ¥100.50", cnyDetail.Description);
        
        var usdDetail = usage.Details?.FirstOrDefault(d => d.Name == "Balance (USD)");
        Assert.NotNull(usdDetail);
        Assert.Equal("$10.00", usdDetail.Used);
        Assert.Contains("Granted: $0.00", usdDetail.Description);
    }

    [Fact]
    public async Task GetUsageAsync_ShouldHandleApiError()
    {
        // Arrange
        var config = new ProviderConfig { ApiKey = "test-key" };
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.Unauthorized });

        // Act
        var result = await _provider.GetUsageAsync(config);
        var usage = result.First();

        // Assert
        Assert.True(usage.IsAvailable);
        Assert.Contains("API Error", usage.Description);
    }
}

