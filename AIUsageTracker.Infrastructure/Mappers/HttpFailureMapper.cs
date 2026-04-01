// <copyright file="HttpFailureMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
using System.Net.Http;
using System.Text.Json;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Mappers;

/// <summary>
/// Centralizes HTTP response and exception classification into <see cref="HttpFailureContext"/> values.
/// This is the single source of truth for "what kind of failure happened?" decisions.
/// Callers retain their own projection policies (which exception to throw, which circuit-breaker
/// state to advance, etc.) — this mapper only answers the classification question.
/// </summary>
public static class HttpFailureMapper
{
    /// <summary>
    /// Classifies an HTTP error response into a structured <see cref="HttpFailureContext"/>.
    /// Only call this for non-success responses; successful responses are not failures.
    /// </summary>
    public static HttpFailureContext ClassifyResponse(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        var context = HttpFailureContext.FromHttpStatus(statusCode);

        if (context.Classification != HttpFailureClassification.RateLimit)
        {
            return context;
        }

        var retryAfter = ExtractRetryAfterDelay(response);
        if (retryAfter is null)
        {
            return context;
        }

        return new HttpFailureContext
        {
            Classification = context.Classification,
            HttpStatus = context.HttpStatus,
            UserMessage = context.UserMessage,
            IsLikelyTransient = context.IsLikelyTransient,
            RetryAfter = retryAfter,
        };
    }

    /// <summary>
    /// Classifies a caught exception into a structured <see cref="HttpFailureContext"/>.
    /// Handles the exception types that occur during provider HTTP calls.
    /// </summary>
    public static HttpFailureContext ClassifyException(Exception exception)
    {
        return exception switch
        {
            TaskCanceledException => HttpFailureContext.FromException(
                exception,
                HttpFailureClassification.Timeout,
                "Request timed out"),
            HttpRequestException => HttpFailureContext.FromException(
                exception,
                HttpFailureClassification.Network,
                "Network error — check connectivity"),
            JsonException => HttpFailureContext.FromException(
                exception,
                HttpFailureClassification.Deserialization,
                "Failed to parse provider response"),
            _ => HttpFailureContext.FromException(
                exception,
                HttpFailureClassification.Unknown,
                "An unexpected error occurred"),
        };
    }

    private static TimeSpan? ExtractRetryAfterDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta.HasValue == true)
        {
            return response.Headers.RetryAfter.Delta.Value;
        }

        if (response.Headers.RetryAfter?.Date.HasValue == true)
        {
            var delay = response.Headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
