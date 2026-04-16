// <copyright file="ClaudeCodeProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
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
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly ClaudeCodeProvider _provider;

    public ClaudeCodeProviderTests()
    {
        this._provider = new ClaudeCodeProvider(this.Logger.Object, this.HttpClient);
        this.Config.ApiKey = TestApiKey;
    }

    /// <summary>
    /// Tests parsing of a typical OAuth usage response with moderate usage.
    /// Response structure based on real API response (anonymized).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
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
        var results = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert — flat cards returned
        Assert.NotNull(results);
        var cards = results!.ToList();
        Assert.Equal(4, cards.Count); // current-session, sonnet, opus, all-models

        foreach (var card in cards)
        {
            Assert.Equal("claude-code", card.ProviderId); // provider-id-guardrail-allow: test assertion
            Assert.Equal("Claude Code", card.ProviderName);
            Assert.True(card.IsQuotaBased);
            Assert.Equal(PlanType.Usage, card.PlanType);
            Assert.NotNull(card.GroupId);
        }

        var currentSession = cards.First(c => string.Equals(c.CardId, "current-session", StringComparison.Ordinal));
        Assert.Equal(35, currentSession.UsedPercent);

        var allModels = cards.First(c => string.Equals(c.CardId, "all-models", StringComparison.Ordinal));
        Assert.Equal(42, allModels.UsedPercent);

        var sonnet = cards.First(c => string.Equals(c.CardId, "sonnet", StringComparison.Ordinal));
        Assert.Equal(48, sonnet.UsedPercent);

        var opus = cards.First(c => string.Equals(c.CardId, "opus", StringComparison.Ordinal));
        Assert.Equal(22, opus.UsedPercent);
    }

    /// <summary>
    /// Tests parsing when user is near 5-hour burst limit.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
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
        var results = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(results);
        var cards = results!.ToList();
        Assert.Equal(4, cards.Count);

        var currentSession = cards.First(c => string.Equals(c.CardId, "current-session", StringComparison.Ordinal));
        Assert.Equal(92, currentSession.UsedPercent);
        Assert.Contains("Extra usage enabled", cards.First(c => string.Equals(c.CardId, "all-models", StringComparison.Ordinal)).Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests parsing when user has high 7-day usage but low burst usage.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
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
        var results = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(results);
        var cards = results!.ToList();

        var sonnet = cards.First(c => string.Equals(c.CardId, "sonnet", StringComparison.Ordinal));
        Assert.Equal(95, sonnet.UsedPercent);

        var opus = cards.First(c => string.Equals(c.CardId, "opus", StringComparison.Ordinal));
        Assert.Equal(45, opus.UsedPercent);
    }

    /// <summary>
    /// Tests parsing of fresh subscription with minimal usage.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
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
        var results = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(results);
        var cards = results!.ToList();
        Assert.True(cards.All(c => c.IsAvailable));

        var allModels = cards.First(c => string.Equals(c.CardId, "all-models", StringComparison.Ordinal));
        Assert.Equal(2, allModels.UsedPercent);
    }

    /// <summary>
    /// Tests parsing when at 100% capacity.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
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
        var results = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(results);
        var cards = results!.ToList();
        Assert.True(cards.All(c => c.UsedPercent == 100));
        Assert.Contains("Extra usage enabled", cards.First(c => string.Equals(c.CardId, "all-models", StringComparison.Ordinal)).Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that current-session card uses the 5-hour reset time, and all-models uses the 7-day reset.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageFromOAuthAsync_ResetTime_PerCardResetTimesCorrectAsync()
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
        var results = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(results);
        var cards = results!.ToList();

        var currentSession = cards.First(c => string.Equals(c.CardId, "current-session", StringComparison.Ordinal));
        Assert.NotNull(currentSession.NextResetTime);
        Assert.True(currentSession.NextResetTime >= fiveHourReset.AddMinutes(-1) && currentSession.NextResetTime <= fiveHourReset.AddMinutes(1));

        var allModels = cards.First(c => string.Equals(c.CardId, "all-models", StringComparison.Ordinal));
        Assert.NotNull(allModels.NextResetTime);
        Assert.True(allModels.NextResetTime >= sevenDayReset.AddMinutes(-1) && allModels.NextResetTime <= sevenDayReset.AddMinutes(1));
    }

    /// <summary>
    /// Tests handling of partial response (missing some fields).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
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
        var results = await this._provider.GetUsageFromOAuthAsync("test-token");

        // Assert
        Assert.NotNull(results);
        var cards = results!.ToList();
        Assert.Single(cards);
        Assert.Equal("current-session", cards[0].CardId);
        Assert.Equal(25, cards[0].UsedPercent);
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
        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        // Assert — two flat cards: current-session and all-models
        Assert.Equal(2, result.Count);
        Assert.All(result, usage =>
        {
            Assert.Equal("claude-code", usage.ProviderId); // provider-id-guardrail-allow: test assertion
            Assert.True(usage.IsQuotaBased);
        });

        var currentSession = result.First(u => string.Equals(u.CardId, "current-session", StringComparison.Ordinal));
        Assert.Equal(45, currentSession.UsedPercent);

        var allModels = result.First(u => string.Equals(u.CardId, "all-models", StringComparison.Ordinal));
        Assert.Equal(60, allModels.UsedPercent);
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
        Assert.Contains("No API key configured", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticDefinition_HasCorrectConfiguration()
    {
        var definition = ClaudeCodeProvider.StaticDefinition;

        Assert.Equal("claude-code", definition.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Claude Code", definition.DisplayName);
        Assert.True(definition.IsQuotaBased);
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
