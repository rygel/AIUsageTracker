// <copyright file="MistralProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure.Providers
{
    using System.Net;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.Exceptions;
    using AIUsageTracker.Infrastructure.Providers;
    using AIUsageTracker.Tests.Infrastructure;
    using Moq;
    using Moq.Protected;
    using Xunit;

    public class MistralProviderTests : HttpProviderTestBase<MistralProvider>
    {
        private readonly MistralProvider _provider;

        public MistralProviderTests()
        {
            this._provider = new MistralProvider(this.HttpClient, this.Logger.Object);
            this.Config.ApiKey = "test-mistral-key";
        }

        [Fact]
        public async Task GetUsageAsync_ValidApiKey_ReturnsConnectedStatus()
        {
            // Arrange
            this.SetupHttpResponse("https://api.mistral.ai/v1/models", new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"data\":[]}")
            });

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.True(usage.IsAvailable);
            Assert.Equal("Mistral AI", usage.ProviderName);
            Assert.Equal("Connected (Check Dashboard)", usage.Description);
            Assert.Equal(200, usage.HttpStatus);
        }

        [Fact]
        public async Task GetUsageAsync_InvalidApiKey_UsesBaseClassErrorMapping()
        {
            // Arrange
            this.SetupHttpResponse("https://api.mistral.ai/v1/models", new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized
            });

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.False(usage.IsAvailable);
            Assert.Equal(401, usage.HttpStatus);
            Assert.Contains("Authentication failed", usage.Description);
        }

        [Fact]
        public async Task GetUsageAsync_Timeout_UsesBaseClassExceptionMapping()
        {
            // Arrange - Force a timeout by throwing TaskCanceledException
            this.SetupHttpResponse(_ => true, null!);
            this.MessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new TaskCanceledException());

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.False(usage.IsAvailable);
            Assert.Contains("timed out", usage.Description, StringComparison.OrdinalIgnoreCase);
        }
    }
}
