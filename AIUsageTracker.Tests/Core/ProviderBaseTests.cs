// <copyright file="ProviderBaseTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Core
{
    using System.Net;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.Providers;
    using AIUsageTracker.Core.Exceptions;
    using Xunit;

    public class ProviderBaseTests
    {
        private class TestProvider : ProviderBase
        {
            public override string ProviderId => "test-provider";
            public override ProviderDefinition Definition => new(
                providerId: "test-provider",
                displayName: "Test Provider",
                planType: PlanType.Coding,
                isQuotaBased: true,
                defaultConfigType: "quota-based");

            public override Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
            {
                throw new NotImplementedException();
            }

            // Expose protected methods for testing
            public ProviderUsage TestCreateUnavailableUsage(string description, int httpStatus = 0)
                => this.CreateUnavailableUsage(description, httpStatus);

            public ProviderUsage TestCreateUnavailableUsageFromStatus(HttpResponseMessage response)
                => this.CreateUnavailableUsageFromStatus(response);

            public ProviderUsage TestCreateUnavailableUsageFromException(Exception ex, string context = "Test context")
                => this.CreateUnavailableUsageFromException(ex, context);

            public ProviderUsage TestCreateUnavailableUsageFromProviderException(ProviderException ex)
                => this.CreateUnavailableUsageFromProviderException(ex);
        }
    `n
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
            Assert.Equal(0, usage.RequestsPercentage);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "Authentication failed (401)")]
        [InlineData(HttpStatusCode.Forbidden, "Access denied (403)")]
        [InlineData(HttpStatusCode.InternalServerError, "Server error (500)")]
        [InlineData(HttpStatusCode.BadRequest, "Request failed (400)")]
        public void CreateUnavailableUsageFromStatus_MapsCodesToDescriptions(HttpStatusCode code, string expectedDescription)
        {
            using var response = new HttpResponseMessage(code);
            var usage = this._provider.TestCreateUnavailableUsageFromStatus(response);

            Assert.Equal(expectedDescription, usage.Description);
            Assert.Equal((int)code, usage.HttpStatus);
        }

        [Fact]
        public void CreateUnavailableUsageFromException_HandlesTimeouts()
        {
            var ex = new TaskCanceledException("Timeout");
            var usage = this._provider.TestCreateUnavailableUsageFromException(ex);

            Assert.Contains("timed out", usage.Description, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CreateUnavailableUsageFromException_HandlesHttpRequestException()
        {
            var ex = new HttpRequestException("Network down");
            var usage = this._provider.TestCreateUnavailableUsageFromException(ex);

            Assert.Contains("Connection failed", usage.Description, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(ProviderErrorType.AuthenticationError, "Authentication failed")]
        [InlineData(ProviderErrorType.RateLimitError, "Rate limit exceeded")]
        [InlineData(ProviderErrorType.TimeoutError, "Request timed out")]
        [InlineData(ProviderErrorType.ServerError, "Server error")]
        [InlineData(ProviderErrorType.DeserializationError, "Failed to parse response")]
        public void CreateUnavailableUsageFromProviderException_MapsErrorTypes(ProviderErrorType type, string expectedSnippet)
        {
            var ex = new ProviderException("test-provider", "Original message", type, 400);
            var usage = this._provider.TestCreateUnavailableUsageFromProviderException(ex);

            Assert.Contains(expectedSnippet, usage.Description);
            Assert.Equal(400, usage.HttpStatus);
        }
    }
}
