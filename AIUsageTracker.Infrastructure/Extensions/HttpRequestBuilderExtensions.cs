using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIUsageTracker.Core.Exceptions;
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
    public static HttpRequestMessage CreateBearerRequest(
        this HttpClient _,
        string url,
        string token,
        string? providerId = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        if (string.IsNullOrWhiteSpace(token))
            throw new ProviderConfigurationException(
                providerId ?? "unknown",
                "Bearer token is null or empty");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return request;
    }

    /// <summary>
    /// Creates a POST request with Bearer token authorization and JSON body.
    /// </summary>
    public static HttpRequestMessage CreateBearerPostRequest<T>(
        this HttpClient _,
        string url,
        string token,
        T body,
        string? providerId = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        if (string.IsNullOrWhiteSpace(token))
            throw new ProviderConfigurationException(
                providerId ?? "unknown",
                "Bearer token is null or empty");

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        return request;
    }

    /// <summary>
    /// Sends a GET request with Bearer token and handles common error patterns.
    /// Maps HTTP status codes and exceptions to specific ProviderException types.
    /// </summary>
    public static async Task<HttpResponseMessage> SendGetBearerAsync(
        this HttpClient httpClient,
        string url,
        string token,
        string providerId,
        TimeSpan? timeout = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        if (string.IsNullOrWhiteSpace(token))
            throw new ProviderConfigurationException(providerId, "API key is missing");

        using var request = httpClient.CreateBearerRequest(url, token, providerId);
        
        var originalTimeout = httpClient.Timeout;
        if (timeout.HasValue)
        {
            httpClient.Timeout = timeout.Value;
        }

        try
        {
            logger?.LogDebug("Sending GET request to {Url} for provider {ProviderId}", url, providerId);
            
            var response = await httpClient.SendAsync(request, cancellationToken);
            
            logger?.LogDebug("Received response {StatusCode} from {Url}", response.StatusCode, url);

            // Map HTTP status codes to specific exceptions
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
            url, token, providerId, timeout, logger, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

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
    /// </summary>
    private static ProviderException MapHttpStatusToException(
        string providerId,
        HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new ProviderAuthenticationException(providerId),
            HttpStatusCode.Forbidden => new ProviderException(
                providerId,
                "Access denied - check API key permissions",
                ProviderErrorType.AuthorizationError,
                statusCode),
            HttpStatusCode.TooManyRequests => new ProviderRateLimitException(
                providerId,
                GetRetryAfter(response)),
            HttpStatusCode.NotFound => new ProviderException(
                providerId,
                "API endpoint not found",
                ProviderErrorType.InvalidResponseError,
                statusCode),
            _ when statusCode >= 500 => new ProviderServerException(
                providerId,
                statusCode,
                $"Server error ({statusCode})"),
            _ => new ProviderException(
                providerId,
                $"Request failed ({statusCode})",
                ProviderErrorType.InvalidResponseError,
                statusCode)
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
