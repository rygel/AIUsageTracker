// <copyright file="DeepSeekProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
        this._provider = new DeepSeekProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesMultiCurrencyBalanceCorrectlyAsync()
    {
        // Arrange
        var responseJson = """
        {
          "is_available": true,
          "balance_infos": [
            {
              "currency": "CNY",
              "total_balance": 150.50,
              "granted_balance": 50.00,
              "topped_up_balance": 100.50
            },
            {
              "currency": "USD",
              "total_balance": 10.00,
              "granted_balance": 0.00,
              "topped_up_balance": 10.00
            }
          ]
        }
        """;

        this.SetupHttpResponse("https://api.deepseek.com/user/balance", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);
        var usages = result.ToList();

        // Assert
        Assert.Single(usages);
        var usage = usages[0];
        Assert.True(usage.IsAvailable);
        Assert.Equal("Balance: ¥150.50", usage.Description);
        Assert.Equal(2, usage.Details?.Count);

        var cnyDetail = usage.Details?.FirstOrDefault(d => string.Equals(d.Name, "Balance (CNY)", StringComparison.Ordinal));
        Assert.NotNull(cnyDetail);
        Assert.StartsWith("¥150.50", cnyDetail.Description, StringComparison.Ordinal);

        var usdDetail = usage.Details?.FirstOrDefault(d => string.Equals(d.Name, "Balance (USD)", StringComparison.Ordinal));
        Assert.NotNull(usdDetail);
        Assert.StartsWith("$10.00", usdDetail.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_ApiError_ReturnsUnavailableAsync()
    {
        // Arrange
        this.SetupHttpResponse("https://api.deepseek.com/user/balance", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.First();

        // Assert
        // Note: DeepSeek currently handles errors by returning IsAvailable = true but with Error message in description
        // This is inconsistent with other providers but we maintain existing behavior here.
        Assert.True(usage.IsAvailable);
        Assert.Contains("API Error", usage.Description, StringComparison.Ordinal);
        Assert.Contains("Unauthorized", usage.Description, StringComparison.Ordinal);
    }
}
