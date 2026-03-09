using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AIUsageTracker.Infrastructure.Http;

public class ResilientHttpClient : IResilientHttpClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy<HttpResponseMessage> _circuitBreakerPolicy;
    private readonly ILogger<ResilientHttpClient> _logger;
    private bool _disposed;

    public ResilientHttpClient(
        HttpClient httpClient,
        ILogger<ResilientHttpClient> logger,
        ResilientHttpClientOptions? options = null)
    {
        this._httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options ?? new ResilientHttpClientOptions();

        // Create retry policy with exponential backoff
        this._retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => opts.RetryStatusCodes.Contains(r.StatusCode))
            .WaitAndRetryAsync(
                opts.MaxRetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(opts.BackoffBase, retryAttempt)),
                onRetry: (outcome, timeSpan, retryCount, context) =>
                {
                    var statusCode = outcome.Result?.StatusCode ?? HttpStatusCode.ServiceUnavailable;
                    this._logger.LogWarning(
                        "HTTP request failed with {StatusCode}. Retrying {RetryCount}/{MaxRetries} in {Delay}s...",
                        statusCode,
                        retryCount,
                        opts.MaxRetryCount,
                        timeSpan.TotalSeconds);
                });

        // Create circuit breaker policy
        this._circuitBreakerPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => opts.CircuitBreakerStatusCodes.Contains(r.StatusCode))
            .CircuitBreakerAsync(
                opts.CircuitBreakerFailureThreshold,
                opts.CircuitBreakerDuration,
                onBreak: (outcome, duration) =>
                {
                    this._logger.LogError(
                        "Circuit breaker opened for {Duration} due to {StatusCode}. Subsequent requests will fail fast.",
                        duration,
                        outcome.Result?.StatusCode ?? HttpStatusCode.ServiceUnavailable);
                },
                onReset: () => this._logger.LogInformation("Circuit breaker closed. Requests will be attempted again."),
                onHalfOpen: () => this._logger.LogDebug("Circuit breaker half-open. Testing if service has recovered..."));
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(ResilientHttpClient));

        return await this._circuitBreakerPolicy
            .WrapAsync(this._retryPolicy)
            .ExecuteAsync(async ct => await this._httpClient.SendAsync(request, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!this._disposed)
        {
            this._httpClient.Dispose();
            this._disposed = true;
        }
    }
}
