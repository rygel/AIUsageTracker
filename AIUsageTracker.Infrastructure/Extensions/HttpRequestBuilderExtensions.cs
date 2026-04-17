// <copyright file="HttpRequestBuilderExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Exceptions;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Mappers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Extensions;

/// <summary>
/// Extension methods for HttpClient to standardize HTTP request patterns and error handling.
/// </summary>
public static class HttpRequestBuilderExtensions
{
    /// <summary>
    /// Creates a GET request with Bearer token authorization.
    /// </summary>
    /// <returns></returns>
    public static HttpRequestMessage CreateBearerRequest(
        this HttpClient httpClient,
        string url,
        string token,
        string? providerId = null)
    {
        _ = httpClient;

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty", nameof(url));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ProviderConfigurationException(
                providerId ?? "unknown",
                "Bearer token is null or empty");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return request;
    }

    /// <summary>
    /// Creates a POST request with Bearer token authorization and JSON body.
    /// </summary>
    /// <returns></returns>
    public static HttpRequestMessage CreateBearerPostRequest<T>(
        this HttpClient httpClient,
        string url,
        string token,
        T body,
        string? providerId = null)
    {
        _ = httpClient;

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty", nameof(url));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ProviderConfigurationException(
                providerId ?? "unknown",
                "Bearer token is null or empty");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return request;
    }

    /// <summary>
    /// Sends a GET request with Bearer token and handles common error patterns.
    /// Maps HTTP status codes and exceptions to specific ProviderException types.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task<HttpResponseMessage> SendGetBearerAsync(
        this HttpClient httpClient,
        string url,
        string token,
        string providerId,
        TimeSpan? timeout = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty", nameof(url));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ProviderConfigurationException(providerId, "API key is missing");
        }

        using var request = httpClient.CreateBearerRequest(url, token, providerId);

        var originalTimeout = httpClient.Timeout;
        if (timeout.HasValue)
        {
            httpClient.Timeout = timeout.Value;
        }

        try
        {
            logger?.LogDebug("Sending GET request to {Url} for provider {ProviderId}", url, providerId);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            logger?.LogDebug("Received response {StatusCode} from {Url}", response.StatusCode, url);

            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpStatusToException(providerId, response);
            }

            return response;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Request to {Url} timed out for provider {ProviderId}", url, providerId);
            throw new ProviderTimeoutException(
                providerId,
                timeout ?? httpClient.Timeout,
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "Network error when calling {Url} for provider {ProviderId}", url, providerId);
            throw new ProviderNetworkException(
                providerId,
                "Connection failed - check network",
                innerException: ex);
        }
        finally
        {
            if (timeout.HasValue)
            {
                httpClient.Timeout = originalTimeout;
            }
        }
    }

    /// <summary>
    /// Sends a GET request and deserializes the JSON response.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task<T?> SendGetBearerAsync<T>(
        this HttpClient httpClient,
        string url,
        string token,
        string providerId,
        TimeSpan? timeout = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.SendGetBearerAsync(
                url,
                token,
                providerId,
                timeout,
                logger,
                cancellationToken)
            .ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            logger?.LogError(ex, "Failed to deserialize response from {Url}: {Json}", url, json);
            throw new ProviderDeserializationException(
                providerId,
                "Failed to parse provider response",
                json,
                ex);
        }
    }

    /// <summary>
    /// Maps HTTP status codes to specific ProviderException types.
    /// Uses <see cref="HttpFailureMapper"/> for failure classification, then applies
    /// provider-layer projection to produce the appropriate typed exception.
    /// </summary>
    private static ProviderException MapHttpStatusToException(
        string providerId,
        HttpResponseMessage response)
    {
        var failure = HttpFailureMapper.ClassifyResponse(response);
        var statusCode = failure.HttpStatus ?? (int)response.StatusCode;

        return failure.Classification switch
        {
            HttpFailureClassification.Authentication =>
                new ProviderAuthenticationException(providerId),

            HttpFailureClassification.Authorization =>
                new ProviderException(
                    providerId,
                    "Access denied - check API key permissions",
                    ProviderErrorType.AuthorizationError,
                    statusCode),

            HttpFailureClassification.RateLimit =>
                new ProviderRateLimitException(providerId, GetRetryAfter(response)),

            HttpFailureClassification.Server =>
                new ProviderServerException(providerId, statusCode, $"Server error ({statusCode.ToString(CultureInfo.InvariantCulture)})"),

            HttpFailureClassification.Client when response.StatusCode == HttpStatusCode.NotFound =>
                new ProviderException(
                    providerId,
                    "API endpoint not found",
                    ProviderErrorType.InvalidResponseError,
                    statusCode),

            _ =>
                new ProviderException(
                    providerId,
                    $"Request failed ({statusCode.ToString(CultureInfo.InvariantCulture)})",
                    ProviderErrorType.InvalidResponseError,
                    statusCode),
        };
    }

    /// <summary>
    /// Extracts Retry-After header value if present.
    /// </summary>
    private static DateTime? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Date.HasValue == true)
        {
            return response.Headers.RetryAfter.Date.Value.UtcDateTime;
        }

        if (response.Headers.RetryAfter?.Delta.HasValue == true)
        {
            return DateTime.UtcNow.Add(response.Headers.RetryAfter.Delta.Value);
        }

        return null;
    }
}
