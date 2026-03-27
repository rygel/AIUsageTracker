// <copyright file="MinimaxProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class MinimaxProviderTests : HttpProviderTestBase<MinimaxProvider>
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private const string ChinaEndpoint = "https://api.minimax.chat/v1/user/usage";
    private const string InternationalEndpoint = "https://api.minimax.io/v1/user/usage";

    private readonly MinimaxProvider _provider;

    public MinimaxProviderTests()
    {
        this._provider = new MinimaxProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
        this.Config.ProviderId = MinimaxProvider.ChinaProviderId;
    }

    /// <summary>
    /// Tests that UsedPercent stores the used percentage for quota-based providers.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_ModerateUsage_ReturnsUsedPercentageAsync()
    {
        // Arrange - 35% used
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 350000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsQuotaBased);

        // UsedPercent stores the actual used ratio: 35% used
        Assert.Equal(35, usage.UsedPercent);
        Assert.Equal(350000, usage.RequestsUsed);
        Assert.Equal(1000000, usage.RequestsAvailable);
    }

    /// <summary>
    /// Tests parsing when user is at high utilization (85% used).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_HighUsage_ReturnsCorrectUsedPercentAsync()
    {
        // Arrange - 85% used
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 850000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();

        // UsedPercent = 85% used
        Assert.Equal(85, usage.UsedPercent);
        Assert.Equal(850000, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests parsing when at 100% capacity.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_AtCapacity_ReturnsFullUsedAsync()
    {
        // Arrange - 100% used
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 1000000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();

        // UsedPercent = 100% used
        Assert.Equal(100, usage.UsedPercent);
        Assert.Equal(1000000, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests parsing with minimal usage (2% used).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_MinimalUsage_ReturnsLowUsedPercentAsync()
    {
        // Arrange - 2% used
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 20000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();

        // UsedPercent = 2% used
        Assert.Equal(2, usage.UsedPercent);
        Assert.Equal(20000, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests parsing with zero usage (0% used).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_ZeroUsage_ReturnsZeroUsedPercentAsync()
    {
        // Arrange - 0% used
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 0,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();

        // UsedPercent = 0% used
        Assert.Equal(0, usage.UsedPercent);
        Assert.Equal(0, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests that usage over 100% is clamped to 100%.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_OverLimit_ClampsToMaxUsedAsync()
    {
        // Arrange - 120% used should clamp to 100%
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 1200000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();

        // UsedPercent should clamp to 100 (not go over)
        Assert.Equal(100, usage.UsedPercent);
        Assert.Equal(1200000, usage.RequestsUsed);
    }

    [Fact]
    public void StaticDefinition_HasCorrectConfiguration()
    {
        var definition = MinimaxProvider.StaticDefinition;

        Assert.Equal("minimax", definition.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Minimax (China)", definition.DisplayName);
        Assert.True(definition.IsQuotaBased);
        Assert.Equal("quota-based", definition.DefaultConfigType);
        Assert.Contains("MINIMAX_API_KEY", definition.DiscoveryEnvironmentVariables);
    }

    [Fact]
    public async Task GetUsageAsync_WithChinaProviderId_UsesChinaEndpointAsync()
    {
        // Arrange
        this.Config.ProviderId = MinimaxProvider.ChinaProviderId;
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 100000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, ChinaEndpoint);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Equal("minimax", usage.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Minimax (China)", usage.ProviderName);
    }

    [Fact]
    public async Task GetUsageAsync_WithInternationalProviderId_UsesInternationalEndpointAsync()
    {
        // Arrange
        this.Config.ProviderId = MinimaxProvider.InternationalProviderId;
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 100000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, InternationalEndpoint);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Equal("minimax-io", usage.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Minimax (International)", usage.ProviderName);
    }

    [Fact]
    public async Task GetUsageAsync_WithLegacyProviderId_UsesInternationalEndpointAsync()
    {
        // Arrange
        this.Config.ProviderId = MinimaxProvider.InternationalLegacyProviderId;
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 100000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, InternationalEndpoint);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Equal("minimax-global", usage.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Minimax (International)", usage.ProviderName);
    }

    [Fact]
    public async Task GetUsageAsync_WithCustomBaseUrl_UsesCustomEndpointAsync()
    {
        // Arrange
        var customUrl = "https://custom.minimax.example.com/v1/user/usage";
        this.Config.BaseUrl = customUrl;
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 50000,
                    "tokens_limit": 500000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, customUrl);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal(10, usage.UsedPercent); // 10% used
    }

    [Fact]
    public async Task GetUsageAsync_NoApiKey_ReturnsUnavailableAsync()
    {
        // Arrange
        this.Config.ApiKey = string.Empty;

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("API Key not found", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_ServerError_ReturnsUnavailableAsync()
    {
        // Arrange
        this.SetupResponse(HttpStatusCode.InternalServerError, """{"error": "internal_error"}""");

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("InternalServerError", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_Unauthorized_ReturnsUnavailableAsync()
    {
        // Arrange
        this.SetupResponse(HttpStatusCode.Unauthorized, """{"error": "invalid_api_key"}""");

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Unauthorized", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_InvalidJson_ReturnsUnavailableAsync()
    {
        // Arrange
        this.SetupResponse(HttpStatusCode.OK, "not valid json");

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Failed to parse", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_MissingUsageField_ReturnsUnavailableAsync()
    {
        // Arrange
        var responseJson = """
            {
                "other_field": "value"
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Invalid Minimax response format", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_RateLimited_ReturnsUnavailableAsync()
    {
        // Arrange
        this.SetupResponse(HttpStatusCode.TooManyRequests, """{"error": "rate_limited"}""");

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("TooManyRequests", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_ZeroLimit_HandlesGracefullyAsync()
    {
        // Arrange - Zero limit should result in 0 utilization
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 0,
                    "tokens_limit": 0
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal(0, usage.UsedPercent); // 0 utilization = 0% used
        Assert.Equal(0, usage.RequestsUsed);
        Assert.Equal(0, usage.RequestsAvailable);
    }

    [Fact]
    public async Task GetUsageAsync_LargeNumbers_ParsesCorrectlyAsync()
    {
        // Arrange - Large token counts
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 45000000000,
                    "tokens_limit": 100000000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal(45, usage.UsedPercent); // 45% used
        Assert.Equal(45000000000, usage.RequestsUsed);
    }

    [Fact]
    public async Task GetUsageAsync_DescriptionFormat_IsCorrectAsync()
    {
        // Arrange
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 500000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();

        // Use culture-independent assertion
        Assert.Contains("tokens used", usage.Description, StringComparison.Ordinal);
        Assert.Contains("limit", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_IsNotCurrencyUsage_ForTokenBasedProviderAsync()
    {
        // Arrange
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 100000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsCurrencyUsage);
    }

    [Fact]
    public async Task GetUsageAsync_PlanType_IsCodingAsync()
    {
        // Arrange
        var responseJson = """
            {
                "usage": {
                    "tokens_used": 100000,
                    "tokens_limit": 1000000
                }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Equal(PlanType.Coding, usage.PlanType);
    }

    private void SetupResponse(HttpStatusCode statusCode, string content, string? url = null)
    {
        url ??= ChinaEndpoint;
        this.SetupHttpResponse(
            r => string.Equals(r.RequestUri?.ToString(), url, StringComparison.Ordinal),
            new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
