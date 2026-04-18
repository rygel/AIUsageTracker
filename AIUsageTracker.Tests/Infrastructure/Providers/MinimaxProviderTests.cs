// <copyright file="MinimaxProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class MinimaxProviderTests : HttpProviderTestBase<MinimaxProvider>
{
    private const string ChinaEndpoint = "https://api.minimax.chat/v1/user/usage";
    private const string InternationalEndpoint = "https://api.minimax.io/v1/user/usage";
    private const string CodingPlanEndpoint = "https://api.minimax.io/v1/api/openplatform/coding_plan/remains";

    private static readonly string TestApiKey = Guid.NewGuid().ToString();

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
        Assert.Equal("MiniMax.io", definition.DisplayName);
        Assert.True(definition.IsQuotaBased);
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
        Assert.Equal("MiniMax.io", usage.ProviderName);
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
        Assert.Equal("MiniMax.io", usage.ProviderName);
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
        Assert.Equal("MiniMax.io", usage.ProviderName);
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

    [Fact]
    public async Task GetUsageAsync_WithCodingPlanProviderId_UsesCodingPlanEndpointAsync()
    {
        // Arrange
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-Text-01",
                        "current_interval_total_count": 100,
                        "current_interval_usage_count": 70,
                        "end_time": 0,
                        "current_weekly_total_count": 500,
                        "current_weekly_usage_count": 400,
                        "weekly_end_time": 0
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        // Act
        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        // Assert — two cards: burst (5h) and weekly
        Assert.Equal(2, result.Count);
        var burst = result.Single(u => u.WindowKind == AIUsageTracker.Core.Models.WindowKind.Burst);
        var weekly = result.Single(u => u.WindowKind == AIUsageTracker.Core.Models.WindowKind.Rolling);

        // 5h: 30 used / 100 total = 30%
        Assert.Equal(30, burst.UsedPercent);
        Assert.Equal(30, burst.RequestsUsed);
        Assert.Equal(100, burst.RequestsAvailable);
        Assert.Equal("minimax-coding-plan", burst.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Minimax.io Coding Plan", burst.ProviderName);

        // Weekly: 100 used / 500 total = 20%
        Assert.Equal(20, weekly.UsedPercent);
        Assert.Equal(100, weekly.RequestsUsed);
        Assert.Equal(500, weekly.RequestsAvailable);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_ApiError_ReturnsUnavailableAsync()
    {
        // Arrange
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        this.SetupResponse(HttpStatusCode.Unauthorized, """{"error":"invalid_key"}""", CodingPlanEndpoint);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Unauthorized", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_NonZeroStatusCode_ReturnsUnavailableAsync()
    {
        // Arrange
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """{"base_resp": {"status_code": 1004, "status_msg": "invalid api key"}}""";
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("invalid api key", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_AtCapacity_Returns100PercentAsync()
    {
        // Arrange — remaining = 0, so 100% used
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-Text-01",
                        "current_interval_total_count": 100,
                        "current_interval_usage_count": 0,
                        "end_time": 0,
                        "current_weekly_total_count": 0,
                        "current_weekly_usage_count": 0,
                        "weekly_end_time": 0
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        // Act
        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        // Assert — only burst card (weekly total = 0 so skipped)
        var burst = result.Single();
        Assert.Equal(100, burst.UsedPercent);
        Assert.Equal(100, burst.RequestsUsed);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_DisplayName_IsMiniMaxCodingPlanAsync()
    {
        // Arrange
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-Text-01",
                        "current_interval_total_count": 100,
                        "current_interval_usage_count": 50,
                        "end_time": 0,
                        "current_weekly_total_count": 0,
                        "current_weekly_usage_count": 0,
                        "weekly_end_time": 0
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        Assert.All(result, u => Assert.Equal("Minimax.io Coding Plan", u.ProviderName));
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_PrefersTextGenerationModelAsync()
    {
        // Arrange: first model is non-text; second model is text generation and must be selected.
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "Image Generation",
                        "current_interval_total_count": 200,
                        "current_interval_usage_count": 150,
                        "end_time": 0,
                        "current_weekly_total_count": 1000,
                        "current_weekly_usage_count": 900,
                        "weekly_end_time": 0
                    },
                    {
                        "model_name": "Text Generation",
                        "current_interval_total_count": 100,
                        "current_interval_usage_count": 80,
                        "end_time": 0,
                        "current_weekly_total_count": 500,
                        "current_weekly_usage_count": 450,
                        "weekly_end_time": 0
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        // Act
        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        // Assert: selected window cards must represent text-generation remaining values.
        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        var weekly = result.Single(u => u.WindowKind == WindowKind.Rolling);

        // 5h: 100 total, 80 remaining -> 20 used
        Assert.Equal(20, burst.RequestsUsed);
        Assert.Equal(100, burst.RequestsAvailable);
        Assert.Equal(20, burst.UsedPercent);

        // Weekly: 500 total, 450 remaining -> 50 used
        Assert.Equal(50, weekly.RequestsUsed);
        Assert.Equal(500, weekly.RequestsAvailable);
        Assert.Equal(10, weekly.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_DoesNotUseSearchModel_WhenTextGenerationExistsAsync()
    {
        // Arrange: both text-generation and search-like models exist; text generation must win.
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "Text Search",
                        "current_interval_total_count": 500,
                        "current_interval_usage_count": 250,
                        "end_time": 0,
                        "current_weekly_total_count": 1000,
                        "current_weekly_usage_count": 900,
                        "weekly_end_time": 0
                    },
                    {
                        "model_name": "Text Generation",
                        "current_interval_total_count": 100,
                        "current_interval_usage_count": 90,
                        "end_time": 0,
                        "current_weekly_total_count": 300,
                        "current_weekly_usage_count": 270,
                        "weekly_end_time": 0
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        // Act
        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        // Assert: exactly one model selected => generic window labels and text-generation math.
        Assert.Equal(2, result.Count);
        Assert.All(result, usage => Assert.DoesNotContain("Search", usage.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, usage => string.Equals(usage.Name, "5h", StringComparison.Ordinal));
        Assert.Contains(result, usage => string.Equals(usage.Name, "Weekly", StringComparison.Ordinal));

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        var weekly = result.Single(u => u.WindowKind == WindowKind.Rolling);

        // Text Generation selected:
        // Burst: 100 total, 90 remaining -> 10 used
        Assert.Equal(10, burst.RequestsUsed);
        Assert.Equal(100, burst.RequestsAvailable);
        Assert.Equal(10, burst.UsedPercent);

        // Weekly: 300 total, 270 remaining -> 30 used
        Assert.Equal(30, weekly.RequestsUsed);
        Assert.Equal(300, weekly.RequestsAvailable);
        Assert.Equal(10, weekly.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_WithoutTextGenerationModel_ReturnsUnavailableAsync()
    {
        // Arrange: no text-generation model in payload -> do not guess from other models.
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "Image Generation",
                        "current_interval_total_count": 200,
                        "current_interval_usage_count": 150,
                        "end_time": 0,
                        "current_weekly_total_count": 1000,
                        "current_weekly_usage_count": 900,
                        "weekly_end_time": 0
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Text Generation model missing", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticDefinition_CodingPlan_IsInAdditionalHandledProviderIds()
    {
        var definition = MinimaxProvider.StaticDefinition;
        Assert.Contains(MinimaxProvider.CodingPlanProviderId, definition.AdditionalHandledProviderIds);
        Assert.Contains(MinimaxProvider.CodingPlanProviderId, definition.SettingsAdditionalProviderIds);
        Assert.Equal("Minimax.io Coding Plan", definition.DisplayNameOverrides[MinimaxProvider.CodingPlanProviderId]);
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
