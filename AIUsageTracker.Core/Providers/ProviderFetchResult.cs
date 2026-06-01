// <copyright file="ProviderFetchResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Providers;

/// <summary>
/// Result of a provider HTTP fetch operation. Either contains the parsed response
/// or a failure with an unavailable <see cref="ProviderUsage"/>.
/// </summary>
public sealed class ProviderFetchResult<TResponse>
    where TResponse : class
{
    private ProviderFetchResult(TResponse? data, ProviderUsage? failure, string rawContent, int httpStatus, bool isSuccess)
    {
        this.Data = data;
        this.FailureUsage = failure;
        this.RawContent = rawContent;
        this.HttpStatus = httpStatus;
        this.IsSuccess = isSuccess;
    }

    public bool IsSuccess { get; }

    public TResponse? Data { get; }

    public ProviderUsage? FailureUsage { get; }

    public string RawContent { get; }

    public int HttpStatus { get; }

    public static ProviderFetchResult<TResponse> Success(TResponse data, string rawContent, int httpStatus)
    {
        return new ProviderFetchResult<TResponse>(data, null, rawContent, httpStatus, true);
    }

    public static ProviderFetchResult<TResponse> Failure(ProviderUsage failure, string rawContent = "")
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new ProviderFetchResult<TResponse>(null, failure, rawContent, failure.HttpStatus, false);
    }
}
