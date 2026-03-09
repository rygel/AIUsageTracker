// <copyright file="AllProvidersWorkingTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.Services;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    public class AllProvidersWorkingTests
    {
        private readonly Mock<IConfigLoader> _mockConfigLoader;
        private readonly Mock<ILogger<ProviderManager>> _mockLogger;

        public AllProvidersWorkingTests()
        {
            this._mockConfigLoader = new Mock<IConfigLoader>();
            this._mockLogger = new Mock<ILogger<ProviderManager>>();
        }

        [Fact]
        public async Task GetAllUsageAsync_ShouldIncludeBothMinimaxVariants()
        {
            // Arrange
            var configs = new List<ProviderConfig>
            {
                new ProviderConfig { ProviderId = "minimax", ApiKey = "dummy-china" },
                new ProviderConfig { ProviderId = "minimax-io", ApiKey = "dummy-intl" }
            };
            this._mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(configs);

            // We need a real MinimaxProvider or a mock that respects the ID
            var mockMinimax = new Mock<IProviderService>();
            mockMinimax.Setup(p => p.ProviderId).Returns("minimax");
            mockMinimax.Setup(p => p.Definition).Returns(new ProviderDefinition(
                providerId: "minimax",
                displayName: "Minimax (China)",
                planType: PlanType.Coding,
                isQuotaBased: true,
                defaultConfigType: "quota-based",
                handledProviderIds: new[] { "minimax", "minimax-io", "minimax-global" }));
            mockMinimax.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
                .ReturnsAsync((ProviderConfig c, Action<ProviderUsage>? callback) => new[] { new ProviderUsage {
                    ProviderId = c.ProviderId,
                    ProviderName = "Minimax",
                    IsAvailable = true
                }});

            var providers = new List<IProviderService> { mockMinimax.Object };
            var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

            // Act
            var results = await manager.GetAllUsageAsync();

            // Assert
            Assert.Contains(results, r => r.ProviderId == "minimax");
            Assert.Contains(results, r => r.ProviderId == "minimax-io");
        }

    }

}
