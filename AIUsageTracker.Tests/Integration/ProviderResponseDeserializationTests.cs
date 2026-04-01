// <copyright file="ProviderResponseDeserializationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIUsageTracker.Tests.Integration;

/// <summary>
/// Tests that real API response JSON formats deserialize correctly through the actual
/// provider parsing pipelines. Uses mock HttpClient to return realistic JSON, but all
/// parsing and ProviderUsage construction is done by the real provider code.
/// </summary>
public class ProviderResponseDeserializationTests
{
    private static readonly string TestApiKey1 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey2 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey3 = Guid.NewGuid().ToString();

    /// <summary>
    /// Codex provider: realistic response with primary_window, secondary_window, and
    /// an additional_rate_limits entry for Spark. Verifies the flat-card output
    /// including burst, weekly, and spark cards.
    /// </summary>
    [Fact]
    public async Task CodexProvider_ParsesRealisticResponse_WithAllWindows()
    {
        // Arrange — realistic Codex/WHAM API response
        var responseJson = """
        {
            "plan_type": "plus",
            "model_name": "o3-pro",
            "rate_limit": {
                "primary_window": {
                    "used_percent": 42.5,
                    "reset_after_seconds": 14400
                },
                "secondary_window": {
                    "used_percent": 28.0,
                    "reset_after_seconds": 432000
                }
            },
            "additional_rate_limits": [
                {
                    "limit_name": "GPT-5.3-Codex-Spark",
                    "model_name": "gpt-5.3-codex-spark",
                    "rate_limit": {
                        "primary_window": {
                            "used_percent": 15.0,
                            "reset_after_seconds": 10800
                        },
                        "secondary_window": {
                            "used_percent": 22.0,
                            "reset_after_seconds": 432000
                        }
                    }
                }
            ],
            "credits": {
                "balance": 150.00,
                "unlimited": false
            }
        }
        """;

        var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
        var logger = NullLogger<CodexProvider>.Instance;

        // Use a non-existent auth file path so the provider skips native auth
        // and uses the config API key directly.
        var provider = new CodexProvider(httpClient, logger, authFilePath: "C:\\nonexistent\\auth.json");

        var config = new ProviderConfig
        {
            ProviderId = "codex",
            ApiKey = TestApiKey1,
        };

        // Act
        var usages = (await provider.GetUsageAsync(config)).ToList();

        // Assert — flat cards: burst, weekly, spark.burst, spark.weekly
        Assert.True(usages.Count >= 4, $"Expected at least 4 flat cards (burst, weekly, spark.burst, spark.weekly), got {usages.Count}");
        Assert.All(usages, u => Assert.True(u.IsAvailable, $"Card {u.CardId} should be available"));
        Assert.All(usages, u => Assert.Equal(200, u.HttpStatus));

        // Burst card (primary_window, 5h)
        var burstCard = usages.First(u => u.CardId == "burst");
        Assert.Equal("codex", burstCard.ProviderId);
        Assert.Equal(42.5, burstCard.UsedPercent, precision: 1);
        Assert.NotNull(burstCard.NextResetTime);

        // Weekly card (secondary_window, 7d)
        var weeklyCard = usages.First(u => u.CardId == "weekly");
        Assert.Equal("codex", weeklyCard.ProviderId);
        Assert.Equal(28.0, weeklyCard.UsedPercent, precision: 1);
        Assert.NotNull(weeklyCard.NextResetTime);

        // Spark cards (additional_rate_limits) — independent provider with burst+rolling
        var sparkBurst = usages.First(u => u.CardId == "spark.burst");
        Assert.Equal("codex.spark", sparkBurst.ProviderId);
        Assert.Contains("Spark", sparkBurst.ProviderName, StringComparison.OrdinalIgnoreCase);
        var sparkWeekly = usages.First(u => u.CardId == "spark.weekly");
        Assert.Equal("codex.spark", sparkWeekly.ProviderId);

        // All cards share the same GroupId for visual grouping
        Assert.All(usages, u => Assert.Equal("codex", u.GroupId));
    }

    /// <summary>
    /// Codex provider: response with ONLY primary_window (no secondary, no spark).
    /// Verifies graceful handling of missing windows.
    /// </summary>
    [Fact]
    public async Task CodexProvider_ParsesMinimalResponse_PrimaryWindowOnly()
    {
        var responseJson = """
        {
            "plan_type": "free",
            "rate_limit": {
                "primary_window": {
                    "used_percent": 85.0,
                    "reset_after_seconds": 7200
                }
            }
        }
        """;

        var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
        var provider = new CodexProvider(httpClient, NullLogger<CodexProvider>.Instance, authFilePath: "C:\\nonexistent\\auth.json");
        var config = new ProviderConfig { ProviderId = "codex", ApiKey = TestApiKey2 };

        var usages = (await provider.GetUsageAsync(config)).ToList();

        // Only burst card expected — no secondary window means no weekly or spark cards
        Assert.Single(usages);
        var burstCard = usages[0];
        Assert.True(burstCard.IsAvailable);
        Assert.Equal("burst", burstCard.CardId);
        Assert.Equal(85.0, burstCard.UsedPercent, precision: 1);

        // No weekly card
        Assert.DoesNotContain(usages, u => u.CardId == "weekly");
    }

    /// <summary>
    /// Codex provider: response where the main rate_limit windows have no reset_after_seconds
    /// but the additional_rate_limits entry has its own reset times.
    /// Verifies that burst and weekly cards have null reset time (no cross-window fallbacks),
    /// while the additional card's own windows carry their reset times.
    /// </summary>
    [Fact]
    public async Task CodexProvider_NoFallback_MainWindowCardsHaveNullResetTime_WhenMainWindowLacksIt()
    {
        var responseJson = """
        {
            "plan_type": "plus",
            "rate_limit": {
                "primary_window": { "used_percent": 30.0 },
                "secondary_window": { "used_percent": 55.0 }
            },
            "additional_rate_limits": [
                {
                    "limit_name": "GPT-5.3-Codex-Spark",
                    "model_name": "gpt-5.3-codex-spark",
                    "rate_limit": {
                        "primary_window": { "used_percent": 30.0, "reset_after_seconds": 9000 },
                        "secondary_window": { "used_percent": 55.0, "reset_after_seconds": 518400 }
                    }
                }
            ]
        }
        """;

        var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
        var provider = new CodexProvider(httpClient, NullLogger<CodexProvider>.Instance, authFilePath: "C:\\nonexistent\\auth.json");
        var config = new ProviderConfig { ProviderId = "codex", ApiKey = TestApiKey3 };

        var usages = (await provider.GetUsageAsync(config)).ToList();

        // Main window cards must use only their own reset_after_seconds — no cross-window fallback.
        var burstCard = usages.First(u => u.CardId == "burst");
        Assert.Null(burstCard.NextResetTime);

        var weeklyCard = usages.First(u => u.CardId == "weekly");
        Assert.Null(weeklyCard.NextResetTime);

        // Spark cards carry their own reset times from additional_rate_limits.
        var sparkBurst = usages.First(u => u.CardId == "spark.burst");
        Assert.NotNull(sparkBurst.NextResetTime);

        var sparkWeekly = usages.First(u => u.CardId == "spark.weekly");
        Assert.NotNull(sparkWeekly.NextResetTime);
    }

    /// <summary>
    /// Claude Code provider: realistic OAuth usage response with five_hour, seven_day,
    /// seven_day_sonnet, and seven_day_opus windows. Verifies flat-card output.
    /// </summary>
    [Fact]
    public async Task ClaudeCodeProvider_ParsesOAuthUsageResponse_WithAllWindows()
    {
        // Arrange — realistic OAuth usage endpoint response
        var resetsAt5h = DateTime.UtcNow.AddHours(3).ToString("o");
        var resetsAt7d = DateTime.UtcNow.AddDays(5).ToString("o");

        var responseJson = $$"""
        {
            "five_hour": {
                "utilization": 35.0,
                "resets_at": "{{resetsAt5h}}"
            },
            "seven_day": {
                "utilization": 48.0,
                "resets_at": "{{resetsAt7d}}"
            },
            "seven_day_sonnet": {
                "utilization": 52.0
            },
            "seven_day_opus": {
                "utilization": 10.0
            },
            "extra_usage": {
                "is_enabled": true
            }
        }
        """;

        var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
        var provider = new ClaudeCodeProvider(NullLogger<ClaudeCodeProvider>.Instance, httpClient);

        // Act — GetUsageFromOAuthAsync returns IEnumerable<ProviderUsage>?
        var results = await provider.GetUsageFromOAuthAsync("test-oauth-token");

        // Assert — flat cards: current-session, sonnet, opus, all-models
        Assert.NotNull(results);
        var cards = results!.ToList();
        Assert.True(cards.Count >= 4, $"Expected at least 4 flat cards, got {cards.Count}");
        Assert.All(cards, c => Assert.Equal("claude-code", c.ProviderId));
        Assert.All(cards, c => Assert.True(c.IsAvailable));

        // Current Session card (5-hour burst)
        var currentSession = cards.First(c => c.CardId == "current-session");
        Assert.Equal(35.0, currentSession.UsedPercent, precision: 1);
        Assert.NotNull(currentSession.NextResetTime);

        // Sonnet card (7-day model-specific)
        var sonnetCard = cards.First(c => c.CardId == "sonnet");
        Assert.Equal(52.0, sonnetCard.UsedPercent, precision: 1);

        // Opus card (7-day model-specific)
        var opusCard = cards.First(c => c.CardId == "opus");
        Assert.Equal(10.0, opusCard.UsedPercent, precision: 1);

        // All Models card (7-day rolling)
        var allModelsCard = cards.First(c => c.CardId == "all-models");
        Assert.Equal(48.0, allModelsCard.UsedPercent, precision: 1);
        Assert.NotNull(allModelsCard.NextResetTime);

        // Extra usage flag should appear in the all-models description
        Assert.Contains("Extra usage enabled", allModelsCard.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Claude Code provider: OAuth response with only five_hour window (no 7-day data).
    /// Verifies graceful handling of partial responses.
    /// </summary>
    [Fact]
    public async Task ClaudeCodeProvider_ParsesPartialResponse_OnlyFiveHour()
    {
        var resetsAt = DateTime.UtcNow.AddHours(4).ToString("o");
        var responseJson = $$"""
        {
            "five_hour": {
                "utilization": 90.0,
                "resets_at": "{{resetsAt}}"
            }
        }
        """;

        var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
        var provider = new ClaudeCodeProvider(NullLogger<ClaudeCodeProvider>.Instance, httpClient);

        var results = await provider.GetUsageFromOAuthAsync("test-token");

        Assert.NotNull(results);
        var cards = results!.ToList();

        // Only the current-session card should be present (no seven_day data)
        Assert.Single(cards);
        var currentSession = cards[0];
        Assert.Equal("current-session", currentSession.CardId);
        Assert.Equal(90.0, currentSession.UsedPercent, precision: 1);

        // No all-models card since seven_day is absent
        Assert.DoesNotContain(cards, c => c.CardId == "all-models");
    }

    /// <summary>
    /// Codex provider: error response with detail message should return unavailable usage.
    /// </summary>
    [Fact]
    public async Task CodexProvider_ErrorResponse_ReturnsUnavailableUsage()
    {
        var responseJson = """
        {
            "detail": "Token expired or invalid"
        }
        """;

        var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
        var provider = new CodexProvider(httpClient, NullLogger<CodexProvider>.Instance, authFilePath: "C:\\nonexistent\\auth.json");
        var config = new ProviderConfig { ProviderId = "codex", ApiKey = TestApiKey3 };

        var usages = (await provider.GetUsageAsync(config)).ToList();

        Assert.Single(usages);
        Assert.False(usages[0].IsAvailable);
        Assert.Contains("Token expired", usages[0].Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Claude Code provider: non-success HTTP response returns null (falls through to next strategy).
    /// </summary>
    [Fact]
    public async Task ClaudeCodeProvider_NonSuccessHttpStatus_ReturnsNull()
    {
        var httpClient = CreateMockHttpClient("{\"error\": \"unauthorized\"}", HttpStatusCode.Unauthorized);
        var provider = new ClaudeCodeProvider(NullLogger<ClaudeCodeProvider>.Instance, httpClient);

        var usage = await provider.GetUsageFromOAuthAsync("bad-token");

        Assert.Null(usage);
    }

    private static HttpClient CreateMockHttpClient(string responseJson, HttpStatusCode statusCode)
    {
        var handler = new MockHttpMessageHandler(responseJson, statusCode);
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost"),
        };
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseJson, HttpStatusCode statusCode)
        {
            this._responseJson = responseJson;
            this._statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(this._statusCode)
            {
                Content = new StringContent(this._responseJson, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
