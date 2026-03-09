// <copyright file="SyntheticProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure.Providers
{
    using System.Net;
    using System.Text.Json;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Infrastructure.Providers;
    using AIUsageTracker.Tests.Infrastructure;
    using Xunit;

    public class SyntheticProviderTests : HttpProviderTestBase<SyntheticProvider>
    {
        private readonly SyntheticProvider _provider;

        public SyntheticProviderTests()
        {
            this._provider = new SyntheticProvider(this.HttpClient, this.Logger.Object);
            this.Config.ApiKey = "test-synthetic-key";
        }

        [Fact]
        public async Task GetUsageAsync_StandardPayload_ParsesCreditsCorrectly()
        {
            // Arrange
            var responseData = new
            {
                subscription = new
                {
                    limit = 1000.0,
                    usage = 250.0,
                    resetAt = "2026-03-10T12:00:00Z"
                }
            };

            this.SetupHttpResponse("https://api.synthetic.new/v2/quotas", new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(responseData))
            });

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.True(usage.IsAvailable);
            Assert.Equal("Synthetic", usage.ProviderName);
            Assert.Equal(75.0, usage.RequestsPercentage); // (1000-250)/1000 * 100
            Assert.Equal(250.0, usage.RequestsUsed);
            Assert.Contains("250 / 1000 credits", usage.Description);
            Assert.NotNull(usage.NextResetTime);
        }

        [Fact]
        public async Task GetUsageAsync_FlatPayload_ParsesSuccessfully()
        {
            // Arrange
            var responseData = new
            {
                total = 500.0,
                consumed = 100.0
            };

            this.SetupHttpResponse("https://api.synthetic.new/v2/quotas", new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(responseData))
            });

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.True(usage.IsAvailable);
            Assert.Equal(80.0, usage.RequestsPercentage);
            Assert.Equal(100.0, usage.RequestsUsed);
        }

        [Fact]
        public async Task GetUsageAsync_NotFoundContent_ReturnsUnavailable()
        {
            // Arrange
            this.SetupHttpResponse("https://api.synthetic.new/v2/quotas", new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK, // API sometimes returns 200 with "Not Found" string
                Content = new StringContent("Not Found")
            });

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.False(usage.IsAvailable);
            Assert.Contains("Invalid key or quota endpoint", usage.Description);
        }
    }
}
