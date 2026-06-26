// <copyright file="ProviderBaseTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.Tests.Core;

public class ProviderBaseTests
{
    private class TestProvider : ProviderBase
    {
        public override string ProviderId => "test-provider";

        public override ProviderDefinition Definition => new(
            providerId: "test-provider",
            displayName: "Test Provider",
            planType: PlanType.Coding,
            isQuotaBased: true);

        public override Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        // Expose protected methods for testing
        public ProviderUsage TestCreateUnavailableUsage(string description, int httpStatus = 0)
            => this.CreateUnavailableUsage(description, httpStatus);

        public ProviderUsage TestCreateUnavailableUsageWithIdentity(string description, string? accountName, int httpStatus = 0)
            => this.CreateUnavailableUsageWithIdentity(description, accountName, httpStatus);

        public string TestDescribeUnavailableStatus(HttpStatusCode statusCode)
            => DescribeUnavailableStatus(statusCode);

        public string TestDescribeUnavailableException(Exception ex, string context = "Test context")
            => DescribeUnavailableException(ex, context);

        public double? TestTryGetHeaderDouble(System.Net.Http.Headers.HttpResponseHeaders headers, string name)
            => TryGetHeaderDouble(headers, name);
    }

    private readonly TestProvider _provider = new();

    [Fact]
    public void CreateUnavailableUsage_SetsCorrectBaseFields()
    {
        var usage = this._provider.TestCreateUnavailableUsage("Error message", 401);

        Assert.Equal("test-provider", usage.ProviderId);
        Assert.Equal("Test Provider", usage.ProviderName);
        Assert.False(usage.IsAvailable);
        Assert.Equal("Error message", usage.Description);
        Assert.Equal(401, usage.HttpStatus);
    }

    [Fact]
    public void CreateUnavailableUsageWithIdentity_SetsAccountName()
    {
        var usage = this._provider.TestCreateUnavailableUsageWithIdentity("Error message", "user@example.com", 401);

        Assert.Equal("user@example.com", usage.AccountName);
        Assert.Equal("Error message", usage.Description);
        Assert.Equal(401, usage.HttpStatus);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Authentication failed (401)")]
    [InlineData(HttpStatusCode.Forbidden, "Access denied (403)")]
    [InlineData(HttpStatusCode.InternalServerError, "Server error (500)")]
    [InlineData(HttpStatusCode.BadRequest, "Request failed (400)")]
    public void DescribeUnavailableStatus_MapsCodesToDescriptions(HttpStatusCode code, string expectedDescription)
    {
        var description = this._provider.TestDescribeUnavailableStatus(code);

        Assert.Equal(expectedDescription, description);
    }

    [Fact]
    public void DescribeUnavailableException_HandlesTimeouts()
    {
        var ex = new TaskCanceledException("Timeout");
        var description = this._provider.TestDescribeUnavailableException(ex);

        Assert.Contains("timed out", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeUnavailableException_HandlesHttpRequestException()
    {
        var ex = new HttpRequestException("Network down");
        var description = this._provider.TestDescribeUnavailableException(ex);

        Assert.Contains("Connection failed", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetHeaderDouble_ValidHeader_ReturnsValue()
    {
        var response = new HttpResponseMessage();
        response.Headers.Add("x-ratelimit-limit-requests", "14400");

        var result = this._provider.TestTryGetHeaderDouble(response.Headers, "x-ratelimit-limit-requests");

        Assert.Equal(14400, result);
    }

    [Fact]
    public void TryGetHeaderDouble_DecimalValue_ReturnsValue()
    {
        var response = new HttpResponseMessage();
        response.Headers.Add("x-ratelimit-reset-requests", "179.56");

        var result = this._provider.TestTryGetHeaderDouble(response.Headers, "x-ratelimit-reset-requests");

        Assert.Equal(179.56, result);
    }

    [Fact]
    public void TryGetHeaderDouble_MissingHeader_ReturnsNull()
    {
        var response = new HttpResponseMessage();

        var result = this._provider.TestTryGetHeaderDouble(response.Headers, "x-ratelimit-limit-requests");

        Assert.Null(result);
    }

    [Fact]
    public void TryGetHeaderDouble_MalformedValue_ReturnsNull()
    {
        var response = new HttpResponseMessage();
        response.Headers.Add("x-ratelimit-limit-requests", "not-a-number");

        var result = this._provider.TestTryGetHeaderDouble(response.Headers, "x-ratelimit-limit-requests");

        Assert.Null(result);
    }
}
