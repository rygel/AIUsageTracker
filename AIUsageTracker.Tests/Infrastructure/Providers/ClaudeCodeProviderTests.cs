// <copyright file="ClaudeCodeProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class ClaudeCodeProviderTests : HttpProviderTestBase<ClaudeCodeProvider>
{
    private readonly ClaudeCodeProvider _provider;

    public ClaudeCodeProviderTests()
    {
        this._provider = new ClaudeCodeProvider(this.Logger.Object, this.HttpClient);
        this.Config.ApiKey = "test-oauth-token";
    }


    /// <summary>
    /// Tests parsing of a typical OAuth usage response with moderate usage.
    /// Response structure based on real API response (anonymized).
    /// </summary>
    [Fact]
    public async Task GetUsageFromOAuthAsync_ModerateUsage_ParsesCorrectlyAsync()
    {
        // Arrange - Anonymized response based on real API data
        var responseJson = """
            {
                "five_hour": {
                    "utilization": 35,
                    "resets_at": "2025-03-13T18:30:00Z"
                },
                "seven_day": {
                    "utilization": 42,
                    "resets_at": "2025-03-20T00:00:00Z"
                },
                "seven_day_sonnet": {
                    "utilization": 48
                },
                "seven_day_opus": {
                    "utilization": 22
                },
                "extra_usage": {
                    "is_enabled": false
                }
            }
            """;

        this.SetupOAuthResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("claude-code", result.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Claude Code", result.ProviderName);
        Assert.True(result.IsQuotaBased);
        Assert.Equal(PlanType.Coding, result.PlanType);

        // UsedPercent is max utilization: max(35%, 42%) = 42% used
        Assert.Equal(42, result.UsedPercent);
        Assert.Equal(42, result.RequestsUsed); // Used percentage stored separately
        Assert.Contains("5h: 35%", result.Description);
        Assert.Contains("7d: 42%", result.Description);
        Assert.NotNull(result.Details);
        Assert.Equal(4, result.Details!.Count);
    }

    /// <summary>
    /// Tests parsing when user is near 5-hour burst limit.
    /// </summary>
    [Fact]
    public async Task GetUsageFromOAuthAsync_NearBurstLimit_ShowsHigherQuotaAsync()
    {
        // Arrange - User hitting 5-hour limit but has 7-day capacity
        var responseJson = """
            {
                "five_hour": {
                    "utilization": 92,
                    "resets_at": "2025-03-13T14:00:00Z"
                },
                "seven_day": {
                    "utilization": 28,
                    "resets_at": "2025-03-18T00:00:00Z"
                },
                "seven_day_sonnet": {
                    "utilization": 30
                },
                "seven_day_opus": {
                    "utilization": 15
                },
                "extra_usage": {
                    "is_enabled": true
                }
            }
            """;

        this.SetupOAuthResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(result);

        // UsedPercent = max(92%, 28%) = 92% used
        Assert.Equal(92, result.UsedPercent);
        Assert.Equal(92, result.RequestsUsed); // Stores the used percentage
        Assert.Contains("Extra usage enabled", result.Description);

        // Verify 5-hour quota is primary
        var primaryDetail = result.Details?.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Burst);
        Assert.NotNull(primaryDetail);
        Assert.Equal("Current Session", primaryDetail!.Name);
        Assert.True(primaryDetail.TryGetPercentageValue(out var percent, out _, out _));
        Assert.Equal(92, percent); // Detail stores actual utilization
    }

    /// <summary>
    /// Tests parsing when user has high 7-day usage but low burst usage.
    /// </summary>
    [Fact]
    public async Task GetUsageFromOAuthAsync_HighWeeklyUsage_ParsesCorrectlyAsync()
    {
        // Arrange - Low burst but high weekly
        var responseJson = """
            {
                "five_hour": {
                    "utilization": 12,
                    "resets_at": "2025-03-13T20:00:00Z"
                },
                "seven_day": {
                    "utilization": 87,
                    "resets_at": "2025-03-15T00:00:00Z"
                },
                "seven_day_sonnet": {
                    "utilization": 95
                },
                "seven_day_opus": {
                    "utilization": 45
                },
                "extra_usage": {
                    "is_enabled": false
                }
            }
            """;

        this.SetupOAuthResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(result);

        // UsedPercent = max(12%, 87%) = 87% used
        Assert.Equal(87, result.UsedPercent);
        Assert.Equal(87, result.RequestsUsed);

        // Verify model breakdown details
        var sonnetDetail = result.Details?.FirstOrDefault(d => d.Name.Contains("Sonnet"));
        Assert.NotNull(sonnetDetail);
        Assert.True(sonnetDetail!.TryGetPercentageValue(out var sonnetPercent, out _, out _));
        Assert.Equal(95, sonnetPercent);

        var opusDetail = result.Details?.FirstOrDefault(d => d.Name.Contains("Opus"));
        Assert.NotNull(opusDetail);
        Assert.True(opusDetail!.TryGetPercentageValue(out var opusPercent, out _, out _));
        Assert.Equal(45, opusPercent);
    }

    /// <summary>
    /// Tests parsing of fresh subscription with minimal usage.
    /// </summary>
    [Fact]
    public async Task GetUsageFromOAuthAsync_MinimalUsage_ParsesCorrectlyAsync()
    {
        // Arrange - New user with minimal usage
        var responseJson = """
            {
                "five_hour": {
                    "utilization": 0,
                    "resets_at": "2025-03-13T22:00:00Z"
                },
                "seven_day": {
                    "utilization": 2,
                    "resets_at": "2025-03-20T00:00:00Z"
                },
                "seven_day_sonnet": {
                    "utilization": 3
                },
                "seven_day_opus": {
                    "utilization": 0
                },
                "extra_usage": {
                    "is_enabled": false
                }
            }
            """;

        this.SetupOAuthResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(result);

        // UsedPercent = max(0%, 2%) = 2% used
        Assert.Equal(2, result.UsedPercent);
        Assert.Equal(2, result.RequestsUsed);
        Assert.True(result.IsAvailable);
    }

    /// <summary>
    /// Tests parsing when at 100% capacity.
    /// </summary>
    [Fact]
    public async Task GetUsageFromOAuthAsync_AtCapacity_ParsesCorrectlyAsync()
    {
        // Arrange - User at capacity
        var responseJson = """
            {
                "five_hour": {
                    "utilization": 100,
                    "resets_at": "2025-03-13T16:30:00Z"
                },
                "seven_day": {
                    "utilization": 100,
                    "resets_at": "2025-03-14T00:00:00Z"
                },
                "seven_day_sonnet": {
                    "utilization": 100
                },
                "seven_day_opus": {
                    "utilization": 100
                },
                "extra_usage": {
                    "is_enabled": true
                }
            }
            """;

        this.SetupOAuthResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(result);

        // UsedPercent = 100% used
        Assert.Equal(100, result.UsedPercent);
        Assert.Equal(100, result.RequestsUsed);
        Assert.Contains("Extra usage enabled", result.Description);
    }

    /// <summary>
    /// Tests that reset time uses the earlier of the two quota resets.
    /// </summary>
    [Fact]
    public async Task GetUsageFromOAuthAsync_ResetTime_UsesEarlierResetAsync()
    {
        // Arrange
        var fiveHourReset = DateTime.UtcNow.AddHours(2);
        var sevenDayReset = DateTime.UtcNow.AddDays(3);

        var responseJson = $$"""
            {
                "five_hour": {
                    "utilization": 50,
                    "resets_at": "{{fiveHourReset:O}}"
                },
                "seven_day": {
                    "utilization": 40,
                    "resets_at": "{{sevenDayReset:O}}"
                },
                "extra_usage": {
                    "is_enabled": false
                }
            }
            """;

        this.SetupOAuthResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.NextResetTime);

        // Should use 5-hour reset since it's earlier
        Assert.True(result.NextResetTime < sevenDayReset);
    }

    /// <summary>
    /// Tests handling of partial response (missing some fields).
    /// </summary>
    [Fact]
    public async Task GetUsageFromOAuthAsync_PartialResponse_HandlesGracefullyAsync()
    {
        // Arrange - Only 5-hour quota present
        var responseJson = """
            {
                "five_hour": {
                    "utilization": 25,
                    "resets_at": "2025-03-13T18:00:00Z"
                }
            }
            """;

        this.SetupOAuthResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(result);

        // UsedPercent = 25% used
        Assert.Equal(25, result.UsedPercent);
        Assert.Equal(25, result.RequestsUsed);
        Assert.Single(result.Details!);
    }



    [Fact]
    public async Task GetUsageFromOAuthAsync_Unauthorized_ReturnsNullAsync()
    {
        // Arrange
        this.SetupOAuthResponse(HttpStatusCode.Unauthorized, """{"error": "invalid_token"}""");

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("invalid-token");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsageFromOAuthAsync_ServerError_ReturnsNullAsync()
    {
        // Arrange
        this.SetupOAuthResponse(HttpStatusCode.InternalServerError, """{"error": "internal_error"}""");

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsageFromOAuthAsync_InvalidJson_ReturnsNullAsync()
    {
        // Arrange
        this.SetupOAuthResponse(HttpStatusCode.OK, "not valid json");

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsageFromOAuthAsync_RateLimited_RetriesOnceAndReturnsNullAsync()
    {
        // Arrange — return a fresh 429 response each time (retry will dispose the first)
        this.MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri != null && r.RequestUri.ToString() == ClaudeCodeProvider.OAuthUsageEndpoint),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error": "rate_limited"}""", System.Text.Encoding.UTF8, "application/json"),
            }));

        // Act
        var result = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert — returns null after retry
        Assert.Null(result);

        // Verify the endpoint was called twice (initial + retry)
        this.MessageHandler.Protected()
            .Verify<Task<HttpResponseMessage>>(
                "SendAsync",
                Moq.Times.Exactly(2),
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri != null && r.RequestUri.ToString() == ClaudeCodeProvider.OAuthUsageEndpoint),
                ItExpr.IsAny<CancellationToken>());
    }



    [Fact]
    public async Task GetUsageAsync_WithOAuthToken_UsesOAuthEndpointFirstAsync()
    {
        // Arrange
        var oauthResponse = """
            {
                "five_hour": {
                    "utilization": 45,
                    "resets_at": "2025-03-13T18:00:00Z"
                },
                "seven_day": {
                    "utilization": 60,
                    "resets_at": "2025-03-20T00:00:00Z"
                },
                "extra_usage": {
                    "is_enabled": false
                }
            }
            """;

        this.SetupOAuthResponse(HttpStatusCode.OK, oauthResponse);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Equal("claude-code", usage.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.True(usage.IsQuotaBased);

        // UsedPercent = max(45%, 60%) = 60% used
        Assert.Equal(60, usage.UsedPercent);
        Assert.Equal(60, usage.RequestsUsed);
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
        Assert.Contains("No API key configured", usage.Description);
    }



    [Fact]
    public void StaticDefinition_HasCorrectConfiguration()
    {
        var definition = ClaudeCodeProvider.StaticDefinition;

        Assert.Equal("claude-code", definition.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Claude Code", definition.DisplayName);
        Assert.True(definition.IsQuotaBased);
        Assert.Equal("quota-based", definition.DefaultConfigType);
        Assert.True(definition.AutoIncludeWhenUnconfigured);
        Assert.Contains("ANTHROPIC_API_KEY", definition.DiscoveryEnvironmentVariables);
        Assert.Contains("CLAUDE_API_KEY", definition.DiscoveryEnvironmentVariables);
    }

    [Fact]
    public void OAuthEndpoint_HasCorrectUrl()
    {
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", ClaudeCodeProvider.OAuthUsageEndpoint);
    }

    [Fact]
    public void OAuthBetaHeader_HasCorrectValue()
    {
        Assert.Equal("oauth-2025-04-20", ClaudeCodeProvider.OAuthBetaHeader);
    }


    private void SetupOAuthResponse(HttpStatusCode statusCode, string content)
    {
        this.SetupHttpResponse(
            r => string.Equals(r.RequestUri?.ToString(), ClaudeCodeProvider.OAuthUsageEndpoint, StringComparison.Ordinal),
            new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
