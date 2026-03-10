// <copyright file="ProviderUsageProcessingPipelineTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderUsageProcessingPipelineTests
{
    private readonly ProviderUsageProcessingPipeline _pipeline;

    public ProviderUsageProcessingPipelineTests()
    {
        var logger = new Mock<ILogger<ProviderUsageProcessingPipeline>>();
        this._pipeline = new ProviderUsageProcessingPipeline(logger.Object);
    }

    [Fact]
    public void Process_WhenDetailContractInvalid_ConvertsUsageToUnavailableError()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = "OpenAI",
            RequestsUsed = 10,
            RequestsAvailable = 100,
            RequestsPercentage = 10,
            IsAvailable = true,
            Details = new[]
            {
                new ProviderUsageDetail
                {
                    Name = string.Empty,
                    DetailType = ProviderUsageDetailType.Unknown,
                    WindowKind = WindowKind.None,
                },
            },
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.False(processed.IsAvailable);
        Assert.True(processed.Description.Contains("Invalid detail contract:", StringComparison.Ordinal));
        Assert.Equal(1, result.DetailContractAdjustedCount);
    }

    [Fact]
    public void Process_WhenApiKeyPlaceholder_DropsUsage()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "anthropic",
            ProviderName = "Anthropic",
            RequestsUsed = 0,
            RequestsAvailable = 0,
            RequestsPercentage = 0,
            IsAvailable = false,
            Description = "API Key missing",
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "anthropic" },
            isPrivacyMode: false);

        Assert.Empty(result.Usages);
        Assert.Equal(1, result.PlaceholderFilteredCount);
    }

    [Fact]
    public void Process_WhenPrivacyModeEnabled_RedactsSensitiveFields()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openrouter",
            ProviderName = "OpenRouter",
            RequestsUsed = 1,
            RequestsAvailable = 10,
            RequestsPercentage = 10,
            IsAvailable = true,
            AccountName = "user@example.com",
            ConfigKey = "config-key",
            RawJson = "{ \"secret\": \"value\" }",
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openrouter" },
            isPrivacyMode: true);

        var processed = Assert.Single(result.Usages);
        Assert.Equal(string.Empty, processed.AccountName);
        Assert.Equal(string.Empty, processed.ConfigKey);
        Assert.Null(processed.RawJson);
        Assert.Equal(1, result.PrivacyRedactedCount);
    }

    [Fact]
    public void Process_WhenPercentageInvalid_NormalizesFromUsageValues()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "zai",
            ProviderName = "Z.AI",
            RequestsUsed = 30,
            RequestsAvailable = 100,
            RequestsPercentage = double.NaN,
            IsQuotaBased = false,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "zai" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.Equal(30d, processed.RequestsPercentage);
        Assert.True(result.NormalizedCount >= 1);
    }

    [Fact]
    public void Process_WhenUsageIsDynamicChildOfActiveProvider_KeepsEntry()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "antigravity.claude-sonnet",
            ProviderName = "Claude Sonnet",
            RequestsUsed = 50,
            RequestsAvailable = 100,
            RequestsPercentage = 50,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "antigravity" },
            isPrivacyMode: false);

        Assert.Single(result.Usages);
        Assert.Equal(0, result.InactiveProviderFilteredCount);
    }

    [Fact]
    public void Process_WhenProviderIdMissing_FiltersInvalidIdentity()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "   ",
            ProviderName = "Invalid",
            RequestsUsed = 10,
            RequestsAvailable = 100,
            RequestsPercentage = 10,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai" },
            isPrivacyMode: false);

        Assert.Empty(result.Usages);
        Assert.Equal(1, result.InvalidIdentityCount);
    }

    [Fact]
    public void Process_WhenUsageNotInActiveProviderSet_FiltersInactiveEntry()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "not-active-provider",
            ProviderName = "Not Active",
            RequestsUsed = 1,
            RequestsAvailable = 10,
            RequestsPercentage = 10,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai", "anthropic" },
            isPrivacyMode: false);

        Assert.Empty(result.Usages);
        Assert.Equal(1, result.InactiveProviderFilteredCount);
    }

    [Fact]
    public void Process_WhenUnavailableWithoutDescription_NormalizesDescriptionAndUtcTimestamp()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = " OpenAI ",
            RequestsUsed = -5,
            RequestsAvailable = -10,
            RequestsPercentage = -20,
            IsAvailable = false,
            Description = " ",
            FetchedAt = default,
            ResponseLatencyMs = -2,
            HttpStatus = 999,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.Equal("OpenAI", processed.ProviderName);
        Assert.Equal(0, processed.RequestsUsed);
        Assert.Equal(0, processed.RequestsAvailable);
        Assert.Equal(0, processed.RequestsPercentage);
        Assert.Equal("Unavailable", processed.Description);
        Assert.Equal(0, processed.ResponseLatencyMs);
        Assert.Equal(0, processed.HttpStatus);
        Assert.NotEqual(default, processed.FetchedAt);
        Assert.Equal(DateTimeKind.Utc, processed.FetchedAt.Kind);
        Assert.True(result.NormalizedCount >= 1);
    }
}
