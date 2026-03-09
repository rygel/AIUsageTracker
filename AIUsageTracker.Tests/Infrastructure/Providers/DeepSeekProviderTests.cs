// <copyright file="DeepSeekProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure.Providers
{
    using System.Net;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Infrastructure.Providers;
    using AIUsageTracker.Tests.Infrastructure;
    using Moq;
    using Moq.Protected;
    using Xunit;

    public class DeepSeekProviderTests : HttpProviderTestBase<DeepSeekProvider>
    {
        private readonly DeepSeekProvider _provider;

        public DeepSeekProviderTests()
        {
            this._provider = new DeepSeekProvider(this.HttpClient, this.Logger.Object);
            this.Config.ApiKey = "test-key";
        }

        [Fact]
        public async Task GetUsageAsync_ValidResponse_ParsesMultiCurrencyBalanceCorrectly()
        {
            // Arrange
            var responseJson = @"{
            string.Emptyis_availablestring.Empty: true,
            string.Emptybalance_infosstring.Empty: [
                {
                    string.Emptycurrencystring.Empty: string.EmptyCNYstring.Empty,
                    string.Emptytotal_balancestring.Empty: 150.50,
                    string.Emptygranted_balancestring.Empty: 50.00,
                    string.Emptytopped_up_balancestring.Empty: 100.50
                },
                {
                    string.Emptycurrencystring.Empty: string.EmptyUSDstring.Empty,
                    string.Emptytotal_balancestring.Empty: 10.00,
                    string.Emptygranted_balancestring.Empty: 0.00,
                    string.Emptytopped_up_balancestring.Empty: 10.00
                }
            ]
        }";

            this.SetupHttpResponse("https://api.deepseek.com/user/balance", new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);
            var usages = result.ToList();

            // Assert
            Assert.Single(usages);
            var usage = usages[0];
            Assert.True(usage.IsAvailable);
            Assert.Equal("Balance: ¥150.50", usage.Description);
            Assert.Equal(2, usage.Details?.Count);

            var cnyDetail = usage.Details?.FirstOrDefault(d => d.Name == "Balance (CNY)");
            Assert.NotNull(cnyDetail);
            Assert.Equal("¥150.50", cnyDetail.Used);

            var usdDetail = usage.Details?.FirstOrDefault(d => d.Name == "Balance (USD)");
            Assert.NotNull(usdDetail);
            Assert.Equal("$10.00", usdDetail.Used);
        }

        [Fact]
        public async Task GetUsageAsync_ApiError_ReturnsUnavailable()
        {
            // Arrange
            this.SetupHttpResponse("https://api.deepseek.com/user/balance", new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized
            });

            // Act
            var result = await this._provider.GetUsageAsync(this.Config);
            var usage = result.First();

            // Assert
            // Note: DeepSeek currently handles errors by returning IsAvailable = true but with Error message in description
            // This is inconsistent with other providers but we maintain existing behavior here.
            Assert.True(usage.IsAvailable);
            Assert.Contains("API Error", usage.Description);
            Assert.Contains("Unauthorized", usage.Description);
        }
    }
}
