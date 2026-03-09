// <copyright file="OpenCodeZenProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure.Providers
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Infrastructure.Providers;
    using AIUsageTracker.Tests.Infrastructure;
    using Xunit;

    public class OpenCodeZenProviderTests : HttpProviderTestBase<OpenCodeZenProvider>
    {
        private readonly OpenCodeZenProvider _provider;

        public OpenCodeZenProviderTests()
        {
            this._provider = new OpenCodeZenProvider(this.Logger.Object);
            this.Config.ApiKey = "test-key";
        }

        [Fact]
        public async Task GetUsageAsync_CliNotFound_ReturnsUnavailable()
        {
            // Arrange
            var provider = new OpenCodeZenProvider(this.Logger.Object, "non-existent-cli");

            // Act
            var result = await provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.False(usage.IsAvailable);
            Assert.Equal(404, usage.HttpStatus);
            Assert.Contains("CLI not found", usage.Description);
        }
    }
}
