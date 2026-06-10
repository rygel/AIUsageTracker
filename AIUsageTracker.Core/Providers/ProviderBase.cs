// <copyright file="ProviderBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageTracker.Core.Exceptions;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Core.Providers;

public abstract class ProviderBase : IProviderService
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected ProviderBase(IProviderDiscoveryService? discoveryService = null)
    {
        this.DiscoveryService = discoveryService;
    }

    public abstract string ProviderId { get; }

    public abstract ProviderDefinition Definition { get; }

    protected IProviderDiscoveryService? DiscoveryService { get; }

    public virtual bool CanHandleProviderId(string providerId) => this.Definition.HandlesProviderId(providerId);

    public abstract Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null,
        CancellationToken cancellationToken = default);

    protected static string FormatResetDescription(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return string.Empty;
        }

        return $"Resets in {((int)resetAfterSeconds.Value).ToString(CultureInfo.InvariantCulture)}s";
    }

    protected static DateTime? ResolveResetTimeFromSeconds(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return null;
        }

        return DateTime.UtcNow.AddSeconds(resetAfterSeconds.Value).ToLocalTime();
    }

    protected static HttpRequestMessage CreateBearerRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    protected static T? DeserializeJsonOrDefault<T>(string content)
        where T : class
    {
        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    protected ProviderUsage CreateUnavailableUsage(
        string description,
        int httpStatus = 0,
        string? authSource = null,
        ProviderUsageState state = ProviderUsageState.Error,
        HttpFailureContext? failureContext = null)
    {
        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = this.Definition.DisplayName ?? this.ProviderId,
            IsAvailable = false,
            Description = description,
            State = state,
            PlanType = this.Definition.PlanType,
            IsQuotaBased = this.Definition.IsQuotaBased,
            AuthSource = authSource ?? string.Empty,
            HttpStatus = httpStatus,
            UsedPercent = 0,
            RequestsUsed = 0,
            RequestsAvailable = 0,
            FailureContext = failureContext,
        };
    }

    protected ProviderUsage CreateUnavailableUsageWithIdentity(
        string description,
        string? accountName,
        int httpStatus = 0,
        string? authSource = null,
        ProviderUsageState state = ProviderUsageState.Error,
        HttpFailureContext? failureContext = null)
    {
        var usage = this.CreateUnavailableUsage(description, httpStatus, authSource, state, failureContext);
        usage.AccountName = accountName ?? string.Empty;
        return usage;
    }

    protected ProviderUsage CreateUnavailableUsageFromStatus(
        HttpResponseMessage response,
        string? authSource = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        var statusCode = (int)response.StatusCode;
        var description = DescribeUnavailableStatus(response.StatusCode);
        var failureContext = HttpFailureContext.FromHttpStatus(statusCode, description);
        return this.CreateUnavailableUsage(description, statusCode, authSource, failureContext: failureContext);
    }

    protected ProviderUsage CreateUnavailableUsageFromException(
        Exception ex,
        string context = "Provider check failed",
        string? authSource = null)
    {
        var message = DescribeUnavailableException(ex, context);
        var classification = ex switch
        {
            TaskCanceledException => HttpFailureClassification.Timeout,
            HttpRequestException => HttpFailureClassification.Network,
            _ => HttpFailureClassification.Unknown,
        };
        var failureContext = HttpFailureContext.FromException(ex, classification, message);
        return this.CreateUnavailableUsage(message, 0, authSource, failureContext: failureContext);
    }

    protected ProviderUsage CreateUnavailableUsageFromProviderException(
        ProviderException ex,
        string? authSource = null)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var description = ex.ErrorType switch
        {
            ProviderErrorType.AuthenticationError => $"Authentication failed ({ex.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"})",
            ProviderErrorType.AuthorizationError => $"Access denied ({ex.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"})",
            ProviderErrorType.NetworkError => "Connection failed - check network",
            ProviderErrorType.TimeoutError => "Request timed out",
            ProviderErrorType.RateLimitError => "Rate limit exceeded - please wait before retrying",
            ProviderErrorType.ServerError => $"Server error ({ex.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"})",
            ProviderErrorType.ConfigurationError => "Configuration error",
            ProviderErrorType.DeserializationError => "Failed to parse response",
            ProviderErrorType.InvalidResponseError => "Invalid response from provider",
            _ => ex.Message,
        };

        var classification = ex.ErrorType switch
        {
            ProviderErrorType.AuthenticationError => HttpFailureClassification.Authentication,
            ProviderErrorType.AuthorizationError => HttpFailureClassification.Authorization,
            ProviderErrorType.NetworkError => HttpFailureClassification.Network,
            ProviderErrorType.TimeoutError => HttpFailureClassification.Timeout,
            ProviderErrorType.RateLimitError => HttpFailureClassification.RateLimit,
            ProviderErrorType.ServerError => HttpFailureClassification.Server,
            ProviderErrorType.DeserializationError => HttpFailureClassification.Deserialization,
            _ => HttpFailureClassification.Unknown,
        };
        var failureContext = HttpFailureContext.FromException(ex, classification, description);
        return this.CreateUnavailableUsage(description, ex.HttpStatusCode ?? 0, authSource, failureContext: failureContext);
    }

    protected static string DescribeUnavailableStatus(HttpStatusCode statusCode)
    {
        var statusCodeValue = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => $"Authentication failed ({statusCodeValue.ToString(CultureInfo.InvariantCulture)})",
            HttpStatusCode.Forbidden => $"Access denied ({statusCodeValue.ToString(CultureInfo.InvariantCulture)})",
            _ when statusCodeValue >= 500 => $"Server error ({statusCodeValue.ToString(CultureInfo.InvariantCulture)})",
            _ => $"Request failed ({statusCodeValue.ToString(CultureInfo.InvariantCulture)})",
        };
    }

    protected static string DescribeUnavailableException(
        Exception ex,
        string context = "Provider check failed")
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex switch
        {
            HttpRequestException => "Connection failed - check network",
            TaskCanceledException => "Request timed out",
            InvalidOperationException => $"Invalid operation: {ex.Message}",
            _ => $"{context}: {ex.Message}",
        };
    }

    /// <summary>
    /// Fetches JSON from a provider endpoint with Bearer auth, deserializes the response,
    /// and handles common HTTP errors. Returns a result that is either the parsed response
    /// or a failure with an unavailable <see cref="ProviderUsage"/>.
    /// </summary>
    protected async Task<ProviderFetchResult<TResponse>> FetchJsonAsync<TResponse>(
        string endpoint,
        ProviderConfig config,
        HttpClient httpClient,
        ILogger logger,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage>? customizeRequest = null)
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            using var request = CreateBearerRequest(HttpMethod.Get, endpoint, config.ApiKey);
            customizeRequest?.Invoke(request);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{ProviderId} API error: {StatusCode}", this.ProviderId, response.StatusCode);
                var statusCode = (int)response.StatusCode;
                var statusDescription = DescribeUnavailableStatus(response.StatusCode);
                return ProviderFetchResult<TResponse>.Failure(
                    this.CreateUnavailableUsage(
                        statusDescription,
                        statusCode,
                        authSource: config.AuthSource,
                        failureContext: HttpFailureContext.FromHttpStatus(statusCode, statusDescription)),
                    content);
            }

            var data = DeserializeJsonOrDefault<TResponse>(content);
            if (data == null)
            {
                return ProviderFetchResult<TResponse>.Failure(
                    this.CreateUnavailableUsage(
                        "Failed to parse response",
                        (int)response.StatusCode,
                        authSource: config.AuthSource,
                        failureContext: new HttpFailureContext
                        {
                            Classification = HttpFailureClassification.Deserialization,
                            HttpStatus = (int)response.StatusCode,
                            UserMessage = "Failed to parse response",
                        }),
                    content);
            }

            return ProviderFetchResult<TResponse>.Success(data, content, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "{ProviderId} check failed", this.ProviderId);
            return ProviderFetchResult<TResponse>.Failure(
                this.CreateUnavailableUsage(
                    "Connection failed - check network",
                    authSource: config.AuthSource,
                    failureContext: HttpFailureContext.FromException(ex, HttpFailureClassification.Network)));
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "{ProviderId} check failed", this.ProviderId);
            return ProviderFetchResult<TResponse>.Failure(
                this.CreateUnavailableUsage(
                    "Request timed out",
                    authSource: config.AuthSource,
                    failureContext: HttpFailureContext.FromException(ex, HttpFailureClassification.Timeout)));
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "{ProviderId} JSON parse failed", this.ProviderId);
            return ProviderFetchResult<TResponse>.Failure(
                this.CreateUnavailableUsage(
                    $"Failed to parse response: {ex.Message}",
                    authSource: config.AuthSource,
                    failureContext: HttpFailureContext.FromException(ex, HttpFailureClassification.Deserialization)));
        }
    }
}
