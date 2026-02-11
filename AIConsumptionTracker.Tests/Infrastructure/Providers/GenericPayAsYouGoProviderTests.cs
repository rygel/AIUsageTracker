using System.Net;
using System.Text.Json;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class GenericPayAsYouGoProviderTests
{
    private readonly Mock<HttpMessageHandler> _msgHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<GenericPayAsYouGoProvider>> _logger;
    private readonly GenericPayAsYouGoProvider _provider;

    public GenericPayAsYouGoProviderTests()
    {
        _msgHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_msgHandler.Object);
        _logger = new Mock<ILogger<GenericPayAsYouGoProvider>>();
        _provider = new GenericPayAsYouGoProvider(_httpClient, _logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_QuotaFormat_ParsesCorrectly()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // Synthetic API response format
        var responseContent = JsonSerializer.Serialize(new
        {
            subscription = new
            {
                limit = 135000.0,
                requests = 35000.0,
                renewsAt = "2024-02-15T00:00:00Z"
            }
        });

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null && 
                    req.RequestUri.AbsoluteUri == "https://api.synthetic.new/v2/quotas"),
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
        Assert.True(usage.IsAvailable);
        Assert.Equal("Synthetic", usage.ProviderName);
        Assert.Equal(PaymentType.Quota, usage.PaymentType);
        Assert.False(usage.IsQuotaBased); // Current implementation has this set to false
        
        // Quota-based: show remaining percentage (74.07% remaining)
        Assert.Equal(74.074, usage.UsagePercentage, 3);
        
        // Description format is "{used:F2} / {total:F2} credits"
        Assert.StartsWith("35000.00 / 135000.00 credits (Resets:", usage.Description);
        
        Assert.NotNull(usage.NextResetTime);
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_PartiallyUsed_ReturnsCorrectPercentage()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // 40% used scenario
        var responseContent = JsonSerializer.Serialize(new
        {
            subscription = new
            {
                limit = 100000.0,
                requests = 40000.0,
                renewsAt = (string?)null
            }
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
        Assert.True(usage.IsAvailable);
        Assert.Equal(PaymentType.Quota, usage.PaymentType);
        
        // Quota-based: show remaining percentage (60% remaining)
        Assert.Equal(60, usage.UsagePercentage);
        
        Assert.Equal("40000.00 / 100000.00 credits", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_NewlyUsed_ReturnsFullQuota()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // Just started, minimal usage
        var responseContent = JsonSerializer.Serialize(new
        {
            subscription = new
            {
                limit = 50000.0,
                requests = 500.0,
                renewsAt = (string?)null
            }
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
        Assert.True(usage.IsAvailable);
        
        // Quota-based: show remaining percentage (99% remaining)
        Assert.Equal(99, usage.UsagePercentage);
        
        Assert.Equal("500.00 / 50000.00 credits", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_NearlyExhausted_ReturnsLowPercentage()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // 90% used - only 10% remaining
        var responseContent = JsonSerializer.Serialize(new
        {
            subscription = new
            {
                limit = 100000.0,
                requests = 90000.0,
                renewsAt = (string?)null
            }
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
        Assert.True(usage.IsAvailable);
        
        // Quota-based: show remaining percentage (10% remaining)
        Assert.Equal(10, usage.UsagePercentage);
        
        Assert.Equal("90000.00 / 100000.00 credits", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_FullyExhausted_ReturnsZero()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // All quota used
        var responseContent = JsonSerializer.Serialize(new
        {
            subscription = new
            {
                limit = 50000.0,
                requests = 50000.0,
                renewsAt = (string?)null
            }
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
        Assert.True(usage.IsAvailable);
        
        // Quota-based: show remaining percentage (0% remaining)
        Assert.Equal(0, usage.UsagePercentage);
        
        Assert.Equal("50000.00 / 50000.00 credits", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_NullSubscription_ThrowsException()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // API returns null subscription
        var responseContent = JsonSerializer.Serialize(new
        {
            subscription = (object?)null
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

        // Act & Assert - Should throw exception for unknown response format
        await Assert.ThrowsAsync<Exception>(async () => await _provider.GetUsageAsync(config));
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_MissingSubscriptionProperty_ThrowsException()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // API returns response without subscription property
        var responseContent = JsonSerializer.Serialize(new
        {
            message = "OK"
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

        // Act & Assert - Should throw exception for unknown response format
        await Assert.ThrowsAsync<Exception>(async () => await _provider.GetUsageAsync(config));
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_InvalidRenewsAt_HandlesGracefully()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // Invalid date format for renewsAt
        var responseContent = JsonSerializer.Serialize(new
        {
            subscription = new
            {
                limit = 100000.0,
                requests = 50000.0,
                renewsAt = "invalid-date-format"
            }
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
        Assert.True(usage.IsAvailable);
        Assert.Equal(50, usage.UsagePercentage);
        Assert.Null(usage.NextResetTime); // Should not crash on invalid date
        Assert.Equal("50000.00 / 100000.00 credits", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_LargeValues_HandlesCorrectly()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        
        // Enterprise plan with large quota
        var responseContent = JsonSerializer.Serialize(new
        {
            subscription = new
            {
                limit = 1000000.0, // 1M requests
                requests = 250000.0, // 250k used
                renewsAt = (string?)null
            }
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
        Assert.True(usage.IsAvailable);
        
        // Quota-based: show remaining percentage (75% remaining)
        Assert.Equal(75, usage.UsagePercentage);
        
        Assert.Equal("250000.00 / 1000000.00 credits", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_ApiError_ThrowsException()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized
            });

        // Act & Assert - Should throw exception for API error
        await Assert.ThrowsAsync<Exception>(async () => await _provider.GetUsageAsync(config));
    }

    [Fact]
    public async Task GetUsageAsync_Synthetic_EmptyResponse_ThrowsException()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "test-key" };
        var responseContent = "{}";

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

        // Act & Assert - Should throw exception for unknown response format
        await Assert.ThrowsAsync<Exception>(async () => await _provider.GetUsageAsync(config));
    }
}
