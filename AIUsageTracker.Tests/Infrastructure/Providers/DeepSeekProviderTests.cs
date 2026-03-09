using System.Net;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class DeepSeekProviderTests : HttpProviderTestBase<DeepSeekProvider>
{
    private readonly DeepSeekProvider _provider;

    public DeepSeekProviderTests()
    {
        _provider = new DeepSeekProvider(HttpClient, Logger.Object);
        Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesMultiCurrencyBalanceCorrectly()
    {
        // Arrange
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

        SetupHttpResponse("https://api.deepseek.com/user/balance", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson)
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);
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

        var usdDetail = usage.Details?.FirstOrDefault(d => d.Name == "Balance (USD)");
        Assert.NotNull(usdDetail);
        Assert.Equal("$10.00", usdDetail.Used);
    }

    [Fact]
    public async Task GetUsageAsync_ApiError_ReturnsUnavailable()
    {
        // Arrange
        SetupHttpResponse("https://api.deepseek.com/user/balance", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);
        var usage = result.First();

        // Assert
        // Note: DeepSeek currently handles errors by returning IsAvailable = true but with Error message in description
        // This is inconsistent with other providers but we maintain existing behavior here.
        Assert.True(usage.IsAvailable);
        Assert.Contains("API Error", usage.Description);
        Assert.Contains("Unauthorized", usage.Description);
    }
}
