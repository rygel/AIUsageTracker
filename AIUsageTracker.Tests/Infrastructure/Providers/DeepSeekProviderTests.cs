// <copyright file="DeepSeekProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class DeepSeekProviderTests : HttpProviderTestBase<DeepSeekProvider>
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly DeepSeekProvider _provider;

    public DeepSeekProviderTests()
    {
        this._provider = new DeepSeekProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
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

        // Assert — DeepSeek now emits one flat card per currency
        Assert.Equal(2, usages.Count);
        Assert.All(usages, u => Assert.True(u.IsAvailable));

        var cnyCard = Assert.Single(usages, u => string.Equals(u.Name, "Balance (CNY)", StringComparison.Ordinal));
        Assert.StartsWith("¥150.50", cnyCard.Description, StringComparison.Ordinal);

        var usdCard = Assert.Single(usages, u => string.Equals(u.Name, "Balance (USD)", StringComparison.Ordinal));
        Assert.StartsWith("$10.00", usdCard.Description, StringComparison.Ordinal);
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

    // --- Phase 4: FailureContext attachment ---
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HttpFailureClassification.Authentication, false)]
    [InlineData(HttpStatusCode.Forbidden, HttpFailureClassification.Authorization, false)]
    [InlineData(HttpStatusCode.TooManyRequests, HttpFailureClassification.RateLimit, true)]
    [InlineData(HttpStatusCode.InternalServerError, HttpFailureClassification.Server, true)]
    public async Task GetUsageAsync_HttpError_AttachesFailureContextWithCorrectClassificationAsync(
        HttpStatusCode statusCode,
        HttpFailureClassification expectedClassification,
        bool expectedTransient)
    {
        this.SetupHttpResponse("https://api.deepseek.com/user/balance", new HttpResponseMessage
        {
            StatusCode = statusCode,
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.First();

        // Output behavior is unchanged
        Assert.True(usage.IsAvailable);
        Assert.Contains("API Error", usage.Description, StringComparison.Ordinal);
        Assert.Equal((int)statusCode, usage.HttpStatus);

        // FailureContext is now attached
        Assert.NotNull(usage.FailureContext);
        Assert.Equal(expectedClassification, usage.FailureContext!.Classification);
        Assert.Equal(expectedTransient, usage.FailureContext.IsLikelyTransient);
    }

    [Fact]
    public async Task GetUsageAsync_NetworkException_AttachesNetworkFailureContextAsync()
    {
        this.MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.First();

        // Output behavior is unchanged
        Assert.False(usage.IsAvailable);
        Assert.Contains("Connection failed", usage.Description, StringComparison.OrdinalIgnoreCase);

        // FailureContext is attached
        Assert.NotNull(usage.FailureContext);
        Assert.Equal(HttpFailureClassification.Network, usage.FailureContext!.Classification);
        Assert.True(usage.FailureContext.IsLikelyTransient);
    }
}
