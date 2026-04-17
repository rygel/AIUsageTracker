// <copyright file="ProviderConnectivityCheckServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderConnectivityCheckServiceTests
{
    private readonly Mock<IConfigService> _configService = new();
    private readonly Mock<IProviderUsageProcessingPipeline> _pipeline = new();

    [Fact]
    public async Task EvaluateAsync_WhenHttpStatusIs400_ReturnsThatErrorStatusAsync()
    {
        var service = this.CreateService();
        this._pipeline
            .Setup(pipeline => pipeline.Process(
                It.IsAny<IEnumerable<ProviderUsage>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                true))
            .Returns(new ProviderUsageProcessingResult
            {
                Usages =
                [
                    new ProviderUsage
                    {
                        ProviderId = "openai",
                        HttpStatus = 400,
                        Description = "Bad request",
                    },
                ],
            });

        var result = await service.EvaluateAsync("openai", []);

        Assert.Equal((false, "Bad request", 400), result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenHttpStatusIs429_ReturnsConnectedAsync()
    {
        var service = this.CreateService();
        this._pipeline
            .Setup(pipeline => pipeline.Process(
                It.IsAny<IEnumerable<ProviderUsage>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                true))
            .Returns(new ProviderUsageProcessingResult
            {
                Usages =
                [
                    new ProviderUsage
                    {
                        ProviderId = "openai",
                        HttpStatus = 429,
                        IsAvailable = true,
                        Description = "Rate limited",
                    },
                ],
            });

        var result = await service.EvaluateAsync("openai", []);

        Assert.Equal((true, "Connected", 200), result);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoUsageReturned_ReturnsServiceUnavailableAsync()
    {
        var service = this.CreateService();
        this._pipeline
            .Setup(pipeline => pipeline.Process(
                It.IsAny<IEnumerable<ProviderUsage>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                true))
            .Returns(new ProviderUsageProcessingResult
            {
                Usages = [],
            });

        var result = await service.EvaluateAsync("openai", []);

        Assert.False(result.Success);
        Assert.Equal(503, result.Status);
        Assert.Contains("no usage data", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAsync_UsesPrivacyPreferenceWhenProcessingAsync()
    {
        var service = this.CreateService(new AppPreferences { IsPrivacyMode = false });
        this._pipeline
            .Setup(pipeline => pipeline.Process(
                It.IsAny<IEnumerable<ProviderUsage>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                false))
            .Returns(new ProviderUsageProcessingResult
            {
                Usages =
                [
                    new ProviderUsage
                    {
                        ProviderId = "openai",
                        IsAvailable = true,
                    },
                ],
            });

        var result = await service.EvaluateAsync("openai", []);

        Assert.Equal((true, "Connected", 200), result);
        this._pipeline.Verify(
            pipeline => pipeline.Process(
                It.IsAny<IEnumerable<ProviderUsage>>(),
                It.Is<IReadOnlyCollection<string>>(ids => ids.Contains("openai", StringComparer.OrdinalIgnoreCase)),
                false),
            Times.Once);
    }

    private ProviderConnectivityCheckService CreateService(AppPreferences? preferences = null)
    {
        this._configService
            .Setup(configService => configService.GetPreferencesAsync())
            .ReturnsAsync(preferences ?? new AppPreferences { IsPrivacyMode = true });

        return new ProviderConnectivityCheckService(this._configService.Object, this._pipeline.Object);
    }
}
