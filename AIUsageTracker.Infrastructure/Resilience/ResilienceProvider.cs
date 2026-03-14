// <copyright file="ResilienceProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Net.Http;

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Http;

using Microsoft.Extensions.Logging;

using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AIUsageTracker.Infrastructure.Resilience;

public class ResilienceProvider : IResilienceProvider
{
    private readonly ILogger<ResilienceProvider> _logger;
    private readonly ConcurrentDictionary<string, object> _policies = new(StringComparer.Ordinal);
    private readonly ResilientHttpClientOptions _defaultOptions;

    public ResilienceProvider(ILogger<ResilienceProvider> logger, ResilientHttpClientOptions? defaultOptions = null)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this._defaultOptions = defaultOptions ?? new ResilientHttpClientOptions();
    }

    public IAsyncPolicy<T> GetPolicy<T>(string policyName)
    {
        return (IAsyncPolicy<T>)this._policies.GetOrAdd(policyName, name => this.CreateDefaultPolicy<T>(name));
    }

    public IAsyncPolicy<T> GetProviderPolicy<T>(string providerId)
    {
        var policyName = $"provider_{providerId}";
        return (IAsyncPolicy<T>)this._policies.GetOrAdd(policyName, name => this.CreateProviderSpecificPolicy<T>(providerId));
    }

    private static bool TryGetHttpResponseMessage<T>(DelegateResult<T> outcome, out HttpResponseMessage? response)
    {
        var resultProperty = outcome.GetType().GetProperty("Result");
        response = resultProperty?.GetValue(outcome) as HttpResponseMessage;
        return response is not null;
    }

    private IAsyncPolicy<T> CreateDefaultPolicy<T>(string name)
    {
        this._logger.LogDebug("Creating new resilience policy: {PolicyName}", name);

        var retryPolicy = Policy<T>
            .Handle<HttpRequestException>()
            .OrResult(r => this.IsRetryable(r))
            .WaitAndRetryAsync(
                this._defaultOptions.MaxRetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(this._defaultOptions.BackoffBase, retryAttempt)),
                onRetry: (outcome, timeSpan, retryCount, _) =>
                {
                    this._logger.LogWarning(
                        "Policy {PolicyName} triggered retry {RetryCount}/{MaxRetries} after {Delay}s due to {Reason}",
                        name,
                        retryCount,
                        this._defaultOptions.MaxRetryCount,
                        timeSpan.TotalSeconds,
                        this.GetReason(outcome));
                });

        var circuitBreakerPolicy = Policy<T>
            .Handle<HttpRequestException>()
            .OrResult(r => this.IsCircuitBreakerTrigger(r))
            .CircuitBreakerAsync(
                this._defaultOptions.CircuitBreakerFailureThreshold,
                this._defaultOptions.CircuitBreakerDuration,
                onBreak: (outcome, duration) =>
                {
                    this._logger.LogError(
                        "Circuit breaker in policy {PolicyName} opened for {Duration} due to {Reason}",
                        name,
                        duration,
                        this.GetReason(outcome));
                },
                onReset: () => this._logger.LogInformation("Circuit breaker in policy {PolicyName} closed.", name),
                onHalfOpen: () => this._logger.LogDebug("Circuit breaker in policy {PolicyName} half-open.", name));

        return circuitBreakerPolicy.WrapAsync(retryPolicy);
    }

    private IAsyncPolicy<T> CreateProviderSpecificPolicy<T>(string providerId)
    {
        return this.CreateDefaultPolicy<T>($"provider_{providerId}");
    }

    private bool IsRetryable(object? result)
    {
        if (result is HttpResponseMessage response)
        {
            return this._defaultOptions.RetryStatusCodes.Contains(response.StatusCode);
        }

        return false;
    }

    private bool IsCircuitBreakerTrigger(object? result)
    {
        if (result is HttpResponseMessage response)
        {
            return this._defaultOptions.CircuitBreakerStatusCodes.Contains(response.StatusCode);
        }

        return false;
    }

    private string GetReason<T>(DelegateResult<T> outcome)
    {
        if (outcome.Exception is not null)
        {
            return outcome.Exception.Message;
        }

        if (TryGetHttpResponseMessage(outcome, out var response))
        {
            return $"HTTP {response!.StatusCode}";
        }

        return "Unknown error";
    }
}
