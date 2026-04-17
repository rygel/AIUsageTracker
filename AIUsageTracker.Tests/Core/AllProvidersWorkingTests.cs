// <copyright file="AllProvidersWorkingTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Core;

public class AllProvidersWorkingTests
{
    private static readonly string TestApiKey1 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey2 = Guid.NewGuid().ToString();

    private readonly Mock<IConfigLoader> _mockConfigLoader;
    private readonly Mock<ILogger<ProviderManager>> _mockLogger;

    public AllProvidersWorkingTests()
    {
        this._mockConfigLoader = new Mock<IConfigLoader>();
        this._mockLogger = new Mock<ILogger<ProviderManager>>();
    }

    [Fact]
    public async Task GetAllUsageAsync_ShouldIncludeBothMinimaxVariantsAsync()
    {
        // Arrange
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "minimax", ApiKey = TestApiKey1 },
            new ProviderConfig { ProviderId = "minimax-io", ApiKey = TestApiKey2 },
        };
        this._mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(configs);

        // We need a real MinimaxProvider or a mock that respects the ID
        var mockMinimax = new Mock<IProviderService>();
        mockMinimax.Setup(p => p.ProviderId).Returns("minimax");
        mockMinimax.Setup(p => p.Definition).Returns(new ProviderDefinition(
            "minimax",
            "MiniMax.com",
            PlanType.Coding,
            isQuotaBased: true)
        {
            AdditionalHandledProviderIds = new[] { "minimax-io", "minimax-global" },
        });
        mockMinimax.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderConfig c, Action<ProviderUsage>? callback, CancellationToken _) => new[]
            {
                new ProviderUsage
                {
                    ProviderId = c.ProviderId,
                    ProviderName = "Minimax",
                    IsAvailable = true,
                },
            });

        var providers = new List<IProviderService> { mockMinimax.Object };
        var manager = new ProviderManager(providers, this._mockConfigLoader.Object, this._mockLogger.Object);

        // Act
        var results = await manager.GetAllUsageAsync();

        // Assert
        Assert.Contains(results, r => string.Equals(r.ProviderId, "minimax", StringComparison.Ordinal));
        Assert.Contains(results, r => string.Equals(r.ProviderId, "minimax-io", StringComparison.Ordinal));
    }
}
