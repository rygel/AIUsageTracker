// <copyright file="HttpFailureMapperTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Mappers;

namespace AIUsageTracker.Tests.Infrastructure;

public class HttpFailureMapperTests
{
    // --- ClassifyResponse: status code mapping ---

    [Theory]
    [InlineData(401, HttpFailureClassification.Authentication, false)]
    [InlineData(403, HttpFailureClassification.Authorization, false)]
    [InlineData(429, HttpFailureClassification.RateLimit, true)]
    [InlineData(500, HttpFailureClassification.Server, true)]
    [InlineData(502, HttpFailureClassification.Server, true)]
    [InlineData(503, HttpFailureClassification.Server, true)]
    [InlineData(400, HttpFailureClassification.Client, false)]
    [InlineData(404, HttpFailureClassification.Client, false)]
    [InlineData(422, HttpFailureClassification.Client, false)]
    public void ClassifyResponse_MapsStatusCodeCorrectly(
        int statusCode,
        HttpFailureClassification expectedClassification,
        bool expectedTransient)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)statusCode);

        var ctx = HttpFailureMapper.ClassifyResponse(response);

        Assert.Equal(expectedClassification, ctx.Classification);
        Assert.Equal(statusCode, ctx.HttpStatus);
        Assert.Equal(expectedTransient, ctx.IsLikelyTransient);
    }

    // --- ClassifyResponse: RetryAfter extraction ---

    [Fact]
    public void ClassifyResponse_ExtractsRetryAfterDelta()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));

        var ctx = HttpFailureMapper.ClassifyResponse(response);

        Assert.Equal(HttpFailureClassification.RateLimit, ctx.Classification);
        Assert.Equal(TimeSpan.FromSeconds(60), ctx.RetryAfter);
    }

    [Fact]
    public void ClassifyResponse_ExtractsRetryAfterDate()
    {
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);

        var ctx = HttpFailureMapper.ClassifyResponse(response);

        Assert.Equal(HttpFailureClassification.RateLimit, ctx.Classification);
        Assert.NotNull(ctx.RetryAfter);
        Assert.True(ctx.RetryAfter!.Value.TotalSeconds > 100); // ~120s minus a small clock delta
    }

    [Fact]
    public void ClassifyResponse_RetryAfterNullWhenNotPresent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var ctx = HttpFailureMapper.ClassifyResponse(response);

        Assert.Equal(HttpFailureClassification.RateLimit, ctx.Classification);
        Assert.Null(ctx.RetryAfter);
    }

    [Fact]
    public void ClassifyResponse_RetryAfterIgnoredForNonRateLimitStatus()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        var ctx = HttpFailureMapper.ClassifyResponse(response);

        Assert.Equal(HttpFailureClassification.Server, ctx.Classification);
        Assert.Null(ctx.RetryAfter);
    }

    // --- ClassifyException: exception type mapping ---

    [Fact]
    public void ClassifyException_TaskCanceledMapsToTimeout()
    {
        var ex = new TaskCanceledException("timeout");

        var ctx = HttpFailureMapper.ClassifyException(ex);

        Assert.Equal(HttpFailureClassification.Timeout, ctx.Classification);
        Assert.True(ctx.IsLikelyTransient);
        Assert.Equal(typeof(TaskCanceledException).FullName, ctx.ExceptionTypeName);
    }

    [Fact]
    public void ClassifyException_HttpRequestExceptionMapsToNetwork()
    {
        var ex = new HttpRequestException("connection refused");

        var ctx = HttpFailureMapper.ClassifyException(ex);

        Assert.Equal(HttpFailureClassification.Network, ctx.Classification);
        Assert.True(ctx.IsLikelyTransient);
        Assert.Equal(typeof(HttpRequestException).FullName, ctx.ExceptionTypeName);
    }

    [Fact]
    public void ClassifyException_JsonExceptionMapsToDeserialization()
    {
        var ex = new JsonException("unexpected token");

        var ctx = HttpFailureMapper.ClassifyException(ex);

        Assert.Equal(HttpFailureClassification.Deserialization, ctx.Classification);
        Assert.False(ctx.IsLikelyTransient);
        Assert.Equal(typeof(JsonException).FullName, ctx.ExceptionTypeName);
    }

    [Fact]
    public void ClassifyException_UnknownExceptionMapsToUnknown()
    {
        var ex = new InvalidOperationException("something unexpected");

        var ctx = HttpFailureMapper.ClassifyException(ex);

        Assert.Equal(HttpFailureClassification.Unknown, ctx.Classification);
        Assert.False(ctx.IsLikelyTransient);
        Assert.Equal(typeof(InvalidOperationException).FullName, ctx.ExceptionTypeName);
    }

    [Fact]
    public void ClassifyException_NoHttpStatusSet()
    {
        var ctx = HttpFailureMapper.ClassifyException(new HttpRequestException());
        Assert.Null(ctx.HttpStatus);
    }
}
