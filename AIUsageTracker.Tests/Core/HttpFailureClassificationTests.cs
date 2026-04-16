// <copyright file="HttpFailureClassificationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Tests.Core;

public class HttpFailureClassificationTests
{
    // --- HttpFailureClassification shape ---
    [Fact]
    public void Classification_Unknown_IsDefaultZero()
    {
        Assert.Equal(0, (int)HttpFailureClassification.Unknown);
        Assert.Equal(default, HttpFailureClassification.Unknown);
    }

    [Theory]
    [InlineData(HttpFailureClassification.Unknown)]
    [InlineData(HttpFailureClassification.Authentication)]
    [InlineData(HttpFailureClassification.Authorization)]
    [InlineData(HttpFailureClassification.RateLimit)]
    [InlineData(HttpFailureClassification.Network)]
    [InlineData(HttpFailureClassification.Timeout)]
    [InlineData(HttpFailureClassification.Server)]
    [InlineData(HttpFailureClassification.Client)]
    [InlineData(HttpFailureClassification.Deserialization)]
    public void Classification_AllExpectedMembersExist(HttpFailureClassification value)
    {
        Assert.True(Enum.IsDefined(value));
    }

    [Fact]
    public void Classification_HasExactlyNineMembers()
    {
        Assert.Equal(9, Enum.GetValues<HttpFailureClassification>().Length);
    }

    // --- HttpFailureContext defaults ---
    [Fact]
    public void Context_DefaultClassificationIsUnknown()
    {
        var ctx = new HttpFailureContext();
        Assert.Equal(HttpFailureClassification.Unknown, ctx.Classification);
    }

    [Fact]
    public void Context_DefaultUserMessageIsEmpty()
    {
        var ctx = new HttpFailureContext();
        Assert.Equal(string.Empty, ctx.UserMessage);
    }

    [Fact]
    public void Context_DefaultNullableFieldsAreNull()
    {
        var ctx = new HttpFailureContext();
        Assert.Null(ctx.HttpStatus);
        Assert.Null(ctx.RetryAfter);
        Assert.Null(ctx.ExceptionTypeName);
        Assert.Null(ctx.DiagnosticNote);
    }

    [Fact]
    public void Context_DefaultIsLikelyTransientIsFalse()
    {
        var ctx = new HttpFailureContext();
        Assert.False(ctx.IsLikelyTransient);
    }

    // --- FromHttpStatus factory ---
    [Theory]
    [InlineData(401, HttpFailureClassification.Authentication, false)]
    [InlineData(403, HttpFailureClassification.Authorization, false)]
    [InlineData(429, HttpFailureClassification.RateLimit, true)]
    [InlineData(500, HttpFailureClassification.Server, true)]
    [InlineData(503, HttpFailureClassification.Server, true)]
    [InlineData(400, HttpFailureClassification.Client, false)]
    [InlineData(404, HttpFailureClassification.Client, false)]
    [InlineData(422, HttpFailureClassification.Client, false)]
    public void FromHttpStatus_MapsCorrectly(int httpStatus, HttpFailureClassification expectedClassification, bool expectedTransient)
    {
        var ctx = HttpFailureContext.FromHttpStatus(httpStatus, "test message");

        Assert.Equal(expectedClassification, ctx.Classification);
        Assert.Equal(httpStatus, ctx.HttpStatus);
        Assert.Equal(expectedTransient, ctx.IsLikelyTransient);
        Assert.Equal("test message", ctx.UserMessage);
    }

    [Fact]
    public void FromHttpStatus_NonFailureStatusMapsToUnknown()
    {
        // 302 is outside the 4xx/5xx failure ranges — classification should be Unknown
        var ctx = HttpFailureContext.FromHttpStatus(302);
        Assert.Equal(HttpFailureClassification.Unknown, ctx.Classification);
        Assert.Equal(302, ctx.HttpStatus);
    }

    [Fact]
    public void FromHttpStatus_EmptyUserMessageIsAllowed()
    {
        var ctx = HttpFailureContext.FromHttpStatus(500);
        Assert.Equal(string.Empty, ctx.UserMessage);
    }

    // --- FromException factory ---
    [Theory]
    [InlineData(HttpFailureClassification.Network, true)]
    [InlineData(HttpFailureClassification.Timeout, true)]
    [InlineData(HttpFailureClassification.Deserialization, false)]
    [InlineData(HttpFailureClassification.Unknown, false)]
    public void FromException_SetsTransientCorrectly(HttpFailureClassification classification, bool expectedTransient)
    {
        var ex = new InvalidOperationException("boom");
        var ctx = HttpFailureContext.FromException(ex, classification, "user message");

        Assert.Equal(classification, ctx.Classification);
        Assert.Equal(expectedTransient, ctx.IsLikelyTransient);
        Assert.Equal("user message", ctx.UserMessage);
    }

    [Fact]
    public void FromException_CapturesExceptionTypeName()
    {
        var ex = new TimeoutException("timed out");
        var ctx = HttpFailureContext.FromException(ex, HttpFailureClassification.Timeout);

        Assert.Equal(typeof(TimeoutException).FullName, ctx.ExceptionTypeName);
    }

    [Fact]
    public void FromException_DoesNotSetHttpStatus()
    {
        var ctx = HttpFailureContext.FromException(new Exception(), HttpFailureClassification.Network);
        Assert.Null(ctx.HttpStatus);
    }

    // --- Immutability ---
    [Fact]
    public void Context_CanBeConstructedWithInitializer()
    {
        var ctx = new HttpFailureContext
        {
            Classification = HttpFailureClassification.RateLimit,
            HttpStatus = 429,
            UserMessage = "Rate limited",
            RetryAfter = TimeSpan.FromSeconds(30),
            IsLikelyTransient = true,
            DiagnosticNote = "Exceeded per-minute quota",
        };

        Assert.Equal(HttpFailureClassification.RateLimit, ctx.Classification);
        Assert.Equal(429, ctx.HttpStatus);
        Assert.Equal("Rate limited", ctx.UserMessage);
        Assert.Equal(TimeSpan.FromSeconds(30), ctx.RetryAfter);
        Assert.True(ctx.IsLikelyTransient);
        Assert.Equal("Exceeded per-minute quota", ctx.DiagnosticNote);
    }
}
