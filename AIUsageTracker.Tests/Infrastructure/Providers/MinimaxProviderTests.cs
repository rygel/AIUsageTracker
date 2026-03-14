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
    private const string ChinaEndpoint = "https://api.minimax.chat/v1/user/usage";
    private const string InternationalEndpoint = "https://api.minimax.io/v1/user/usage";

    private readonly MinimaxProvider _provider;

    public MinimaxProviderTests()
    {
        this._provider = new MinimaxProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = "test-api-key";
        this.Config.ProviderId = MinimaxProvider.ChinaProviderId;
    }

    #region Quota Percentage Semantic Tests

    /// <summary>
    /// Tests that RequestsPercentage stores REMAINING percentage for quota-based providers.
    /// This is critical for correct UI display where usedPercent = 100 - RequestsPercentage.
    /// </summary>
    [Fact]
    public async Task GetUsageAsync_ModerateUsage_ReturnsRemainingPercentageAsync()
    {
        // Arrange - 35% used means 65% remaining
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

        // CRITICAL: For quota-based providers, RequestsPercentage is REMAINING (100 - used)
        // 35% used = 65% remaining
        Assert.Equal(65, usage.RequestsPercentage);
        Assert.Equal(350000, usage.RequestsUsed);
        Assert.Equal(1000000, usage.RequestsAvailable);
    }

    /// <summary>
    /// Tests parsing when user is at high utilization (85% used = 15% remaining).
    /// </summary>
    [Fact]
    public async Task GetUsageAsync_HighUsage_ReturnsCorrectRemainingAsync()
    {
        // Arrange - 85% used means 15% remaining
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

        // For quota-based: RequestsPercentage = 100 - 85 = 15% remaining
        Assert.Equal(15, usage.RequestsPercentage);
        Assert.Equal(850000, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests parsing when at 100% capacity (0% remaining).
    /// </summary>
    [Fact]
    public async Task GetUsageAsync_AtCapacity_ReturnsZeroRemainingAsync()
    {
        // Arrange - 100% used means 0% remaining
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

        // For quota-based: RequestsPercentage = 100 - 100 = 0% remaining
        Assert.Equal(0, usage.RequestsPercentage);
        Assert.Equal(1000000, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests parsing with minimal usage (2% used = 98% remaining).
    /// </summary>
    [Fact]
    public async Task GetUsageAsync_MinimalUsage_ReturnsHighRemainingAsync()
    {
        // Arrange - 2% used means 98% remaining
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

        // For quota-based: RequestsPercentage = 100 - 2 = 98% remaining
        Assert.Equal(98, usage.RequestsPercentage);
        Assert.Equal(20000, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests parsing with zero usage (0% used = 100% remaining).
    /// </summary>
    [Fact]
    public async Task GetUsageAsync_ZeroUsage_ReturnsFullRemainingAsync()
    {
        // Arrange - 0% used means 100% remaining
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

        // For quota-based: RequestsPercentage = 100 - 0 = 100% remaining
        Assert.Equal(100, usage.RequestsPercentage);
        Assert.Equal(0, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests that usage over 100% is clamped to 0% remaining.
    /// </summary>
    [Fact]
    public async Task GetUsageAsync_OverLimit_ClampsToZeroRemainingAsync()
    {
        // Arrange - 120% used should clamp to 0% remaining
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

        // For quota-based: RequestsPercentage should clamp to 0 (not go negative)
        Assert.Equal(0, usage.RequestsPercentage);
        Assert.Equal(1200000, usage.RequestsUsed);
    }

    #endregion

    #region Provider Configuration Tests

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
        Assert.Equal(90, usage.RequestsPercentage); // 10% used = 90% remaining
    }

    #endregion

    #region Error Handling Tests

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
        Assert.Contains("API Key not found", usage.Description);
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
        Assert.Contains("InternalServerError", usage.Description);
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
        Assert.Contains("Unauthorized", usage.Description);
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
        Assert.Contains("Failed to parse", usage.Description);
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
        Assert.Contains("Invalid Minimax response format", usage.Description);
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
        Assert.Contains("TooManyRequests", usage.Description);
    }

    #endregion

    #region Edge Cases

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
        Assert.Equal(100, usage.RequestsPercentage); // 0 utilization = 100% remaining
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
        Assert.Equal(55, usage.RequestsPercentage); // 45% used = 55% remaining
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
    public async Task GetUsageAsync_UsageUnit_IsTokensAsync()
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
        Assert.Equal("Tokens", usage.UsageUnit);
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

    #endregion

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
