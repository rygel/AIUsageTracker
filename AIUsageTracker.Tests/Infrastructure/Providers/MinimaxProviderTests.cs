// <copyright file="MinimaxProviderTests.cs" company="AIUsageTracker">
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

    public class MinimaxProviderTests : HttpProviderTestBase<MinimaxProvider>
    {
        private readonly MinimaxProvider _provider;

        public MinimaxProviderTests()
        {
            this._provider = new MinimaxProvider(this.HttpClient, this.Logger.Object);
            this.Config.ApiKey = "test-key";
        }

        [Fact]
        public async Task GetUsageAsync_ValidResponse_ParsesUsageCorrectly()
        {
            // Arrange
            var responseData = new
            {
                usage = new
                {
                    tokens_used = 30.0,
                    tokens_limit = 100.0
                }
            };

            this.SetupHttpResponse("https://api.minimax.chat/v1/user/usage", new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(responseData))
            });

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.True(usage.IsAvailable);
            Assert.Contains("30", usage.RequestsUsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Contains("100", usage.RequestsAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
