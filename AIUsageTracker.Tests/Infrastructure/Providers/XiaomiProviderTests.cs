using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class XiaomiProviderTests
{
    private readonly Mock<HttpMessageHandler> _msgHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<XiaomiProvider>> _logger;
    private readonly XiaomiProvider _provider;

    public XiaomiProviderTests()
    {
        _msgHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_msgHandler.Object);
        _logger = new Mock<ILogger<XiaomiProvider>>();
        _provider = new XiaomiProvider(_httpClient, _logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_ValidQuotaResponse_CalculatesPercentageCorrectly()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "xiaomi", ApiKey = "test-key" };
        
        // 200 quota, 150 balance (remaining). RequestsPercentage uses remaining semantics.
        var responseContent = JsonSerializer.Serialize(new
        {
            code = 0,
            data = new { balance = 150, quota = 200 }
        });

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
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
        Assert.Equal("Xiaomi", usage.ProviderName);
        Assert.Equal(75, usage.RequestsPercentage);
        Assert.Equal(50, usage.RequestsUsed);
        Assert.Equal(200, usage.RequestsAvailable);
        Assert.True(usage.IsQuotaBased);
        Assert.Contains("150 remaining", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_NoQuotaResponse_ReturnsBalanceOnly()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "xiaomi", ApiKey = "test-key" };
        
        // 0 quota (pay as you go maybe?), 50 balance
        var responseContent = JsonSerializer.Serialize(new
        {
            code = 0,
            data = new { balance = 50, quota = 0 }
        });

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
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
        Assert.Equal("Xiaomi", usage.ProviderName);
        Assert.Equal(0, usage.RequestsPercentage);
        Assert.Equal(0, usage.RequestsUsed); // Since quota is 0, cost used logic was quota - balance = -50 -> 0 clamped? No, logic is "quota > 0 ? quota - balance : 0"
        
        // Recalculating expected based on code:
        // Limit = quota > 0 ? quota : balance => 50
        // PlanType = UsageBased
        Assert.Equal(50, usage.RequestsAvailable); 
        Assert.False(usage.IsQuotaBased);
        Assert.Contains("Balance: 50", usage.Description);
    }
}

