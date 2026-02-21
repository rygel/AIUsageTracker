using System.Net;
using System.Text.Json;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class ZaiProviderTests
{
    private readonly Mock<HttpMessageHandler> _msgHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<ZaiProvider>> _logger;
    private readonly ZaiProvider _provider;

    public ZaiProviderTests()
    {
        _msgHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_msgHandler.Object);
        _logger = new Mock<ILogger<ZaiProvider>>();
        _provider = new ZaiProvider(_httpClient, _logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_InvertedCalculation_ReturnsCorrectUsedPercentage()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "zai-coding-plan", ApiKey = "test-key" };
        
        // Scenario: 
        // Total Limit = 100
        // Current Value (Remaining) = 90
        // Expected Used = 10%
        
        // If the current implementation is inverted, it calculates 90/100 * 100 = 90%.
        // The fix should calculate (100-90)/100 * 100 = 10%.

        // Assuming JSON structure matches ZaiProvider.cs
        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = (double?)null, 
                        currentValue = 0, // Unused
                        usage = 100 // mapped to Total property
                    }
                }
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
        Assert.Equal("Z.AI Coding Plan", usage.ProviderName);

        // Quota-based providers show REMAINING percentage (full bar = lots remaining)
        // CurrentValue = 0 (Used), Total = 100.
        // Expected Percentage = 100% remaining. (Full Bar = all quota available)
        Assert.Equal(100, usage.RequestsPercentage);

        Assert.Contains("Remaining", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_20PercentUsed_ReturnsCorrectPercentage()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "zai-coding-plan", ApiKey = "test-key" };
        
        // Scenario: 20% used of quota
        // Total Limit = 135M tokens
        // Current Value (Used) = 27M tokens (20%)
        // Remaining = 108M tokens (80%)

        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = (double?)null, 
                        currentValue = 27000000L, // 20% used
                        usage = 135000000L, // Total limit
                        remaining = 108000000L
                    }
                }
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
        Assert.Equal("Z.AI Coding Plan (Ultra/Enterprise)", usage.ProviderName);

        // Quota-based providers show REMAINING percentage (80% remaining)
        Assert.Equal(80, usage.RequestsPercentage);

        // Description should show "80.0% Remaining"
        Assert.Contains("80.0% Remaining", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_NullTotalValue_ReturnsUnknownUsage()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "zai-coding-plan", ApiKey = "test-key" };
        
        // This test catches the error that occurred in production where Total was null
        // Real API response might have missing Total field
        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = (double?)null, 
                        currentValue = 1000000L, // Some value used
                        usage = (long?)null, // Total is null - this was causing the error
                        remaining = (long?)null
                    }
                }
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

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Usage unknown", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_RealisticApiResponse_WithNextResetTime()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "zai-coding-plan", ApiKey = "test-key" };
        
        // This mimics a realistic Z.AI API response with multiple limits
        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new object[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = (double?)null,
                        currentValue = 35000000L, // 35M used
                        usage = 135000000L, // 135M total
                        remaining = 100000000L, // 100M remaining
                        nextResetTime = 1773532800L // Mar 15, 2026 (Unix seconds)
                    },
                    new
                    {
                        type = "TIME_LIMIT",
                        percentage = 25.5, // 25.5% used
                        currentValue = (long?)null,
                        usage = (long?)null,
                        remaining = (long?)null,
                        nextResetTime = 1773532800L // Mar 15, 2026 (Unix seconds)
                    }
                }
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
        Assert.Equal("Z.AI Coding Plan (Ultra/Enterprise)", usage.ProviderName);
        Assert.Equal(74.074, usage.RequestsPercentage, 3); // ~74% remaining (100M/135M) with tolerance
        Assert.Contains("74.1% Remaining of 135M tokens limit", usage.Description);
        Assert.NotNull(usage.NextResetTime);
    }

    [Fact]
    public async Task GetUsageAsync_CompleteRealisticResponse_ProPlan()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "zai-coding-plan", ApiKey = "test-key" };
        
        // Complete realistic response for Pro plan
        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = (double?)null,
                        currentValue = 15000000L, // 15M used
                        usage = 12000000L, // 12M total (Pro plan)
                        remaining = (long?)null,
                        nextResetTime = (long?)null
                    }
                }
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
        Assert.Equal("Z.AI Coding Plan (Pro)", usage.ProviderName);
        // When used > total, remaining is negative - shows overage situation
        Assert.Contains("Remaining of 12M tokens limit", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_NoLimits_ReturnsUnavailable()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "zai-coding-plan", ApiKey = "test-key" };
        
        // API returns empty limits array
        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new object[0]
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
        Assert.False(usage.IsAvailable);
        Assert.Contains("No usage data available", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_NullData_HandlesGracefully()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "zai-coding-plan", ApiKey = "test-key" };
        
        // API returns null data
        var responseContent = JsonSerializer.Serialize(new
        {
            data = (object?)null
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
        Assert.False(usage.IsAvailable);
        Assert.Contains("No usage data available", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_MultipleLimits_SelectsActiveOrLargest()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "zai-coding-plan", ApiKey = "test-key" };
        
        // Scenario: Two limits returned.
        // 1. New/Active limit: 100% remaining.
        // 2. Old/Exhausted limit: 0 remaining.
        // The provider should ideally pick the active one, or at least the one that makes sense.
        // If it picks the first one (Active), it's good. 
        // But what if the order is swapped? Can we test robustness?

        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new object[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = (double?)null,
                        currentValue = 100000000L, // 100M Used -> 0 Remaining (Exhausted)
                        usage = 100000000L, // 100M Total
                        remaining = 0L, 
                        nextResetTime = 1700000000L // Past
                    },
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = (double?)null,
                        currentValue = 0L, // 0 Used -> 100M Remaining (Active)
                        usage = 100000000L, // 100M Total
                        remaining = 100000000L, 
                        nextResetTime = 4900000000L // Future
                    }
                }
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
        
        // We expect it to be smart enough to pick the ACTIVE limit (100% remaining)
        // rather than the first exhausted limit it finds (0% remaining).
        Assert.Equal(100, usage.RequestsPercentage); 
        Assert.Contains("Remaining", usage.Description);
    }
}
