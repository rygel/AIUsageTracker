using AIUsageTracker.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace AIUsageTracker.Infrastructure.Extensions;

public static class HttpClientExtensions
{
    /// <summary>
    /// Adds HttpClient with Polly retry and circuit breaker policies
    /// </summary>
    public static IServiceCollection AddResilientHttpClient(this IServiceCollection services)
    {
        // Configure retry policy
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timeSpan, retryCount, context) =>
                {
                    // Logging is handled by the providers
                });

        // Configure circuit breaker policy
        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                5,
                TimeSpan.FromSeconds(30));

        // Add named HttpClient with policies
        services.AddHttpClient("ResilientClient")
            .AddPolicyHandler(retryPolicy)
            .AddPolicyHandler(circuitBreakerPolicy);

        // Add default HttpClient with policies  
        // Note: AddHttpClient() without name returns IServiceCollection, so we need to configure it differently
        services.AddHttpClient(string.Empty) // Empty string = default client
            .AddPolicyHandler(retryPolicy)
            .AddPolicyHandler(circuitBreakerPolicy);

        // Register ResilientHttpClient as transient
        services.AddTransient<IResilientHttpClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("ResilientClient");
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientHttpClient>>();
            return new ResilientHttpClient(httpClient, logger);
        });

        return services;
    }
}
