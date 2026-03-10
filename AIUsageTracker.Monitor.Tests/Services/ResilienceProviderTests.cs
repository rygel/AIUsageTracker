using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AIUsageTracker.Infrastructure.Http;
using AIUsageTracker.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Moq;
using Polly;
using Xunit;

namespace AIUsageTracker.Monitor.Tests.Services;

public class ResilienceProviderTests
{
    private readonly Mock<ILogger<ResilienceProvider>> _mockLogger;
    private readonly ResilienceProvider _provider;

    public ResilienceProviderTests()
    {
        _mockLogger = new Mock<ILogger<ResilienceProvider>>();
        _provider = new ResilienceProvider(_mockLogger.Object);
    }

    [Fact]
    public void GetPolicy_ReturnsWrappedPolicy()
    {
        // Act
        var policy = _provider.GetPolicy<HttpResponseMessage>("test_policy");

        // Assert
        Assert.NotNull(policy);
    }

    [Fact]
    public async Task Policy_RetriesOnRetryableStatusCode()
    {
        // Arrange
        var options = new ResilientHttpClientOptions
        {
            MaxRetryCount = 2,
            BackoffBase = 0.1, // Fast for tests
            RetryStatusCodes = new[] { HttpStatusCode.TooManyRequests }
        };
        var provider = new ResilienceProvider(_mockLogger.Object, options);
        var policy = provider.GetPolicy<HttpResponseMessage>("retry_test");
        
        int callCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            callCount++;
            if (callCount < 3)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        // Assert
        Assert.Equal(3, callCount);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Policy_CircuitBreakerOpensOnFailures()
    {
        // Arrange
        var options = new ResilientHttpClientOptions
        {
            MaxRetryCount = 0, // No retries for this test
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerDuration = TimeSpan.FromSeconds(5),
            CircuitBreakerStatusCodes = new[] { HttpStatusCode.InternalServerError }
        };
        var provider = new ResilienceProvider(_mockLogger.Object, options);
        var policy = provider.GetPolicy<HttpResponseMessage>("circuit_test");

        // Act
        // 1st failure
        await policy.ExecuteAsync(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        // 2nd failure - should trip
        await policy.ExecuteAsync(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // 3rd call - should throw BrokenCircuitException
        await Assert.ThrowsAsync<Polly.CircuitBreaker.BrokenCircuitException<HttpResponseMessage>>(() => 
            policy.ExecuteAsync(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
    }
}
