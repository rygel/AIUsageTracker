// <copyright file="ProviderResponseDeserializationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
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
    /// <summary>
    /// Codex provider: realistic response with primary_window, secondary_window, and
    /// an additional_rate_limits entry for Spark. Verifies the full ProviderUsage output
    /// including details for burst, weekly, and spark windows.
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
            ApiKey = "test-access-token",
        };

        // Act
        var usages = (await provider.GetUsageAsync(config)).ToList();

        // Assert — should return exactly one ProviderUsage
        Assert.Single(usages);
        var usage = usages[0];

        Assert.Equal("codex", usage.ProviderId);
        Assert.True(usage.IsAvailable, "Usage should be available");
        Assert.Equal(200, usage.HttpStatus);

        // Effective used percent should be the max across all windows:
        // primary=42.5, secondary=28.0, spark_primary=15.0, spark_secondary=22.0 → max=42.5
        Assert.Equal(42.5, usage.UsedPercent, precision: 1);

        // NextResetTime should be set (from the weekly/secondary window)
        Assert.NotNull(usage.NextResetTime);

        // Details should include burst (5h), weekly, and spark windows
        Assert.NotNull(usage.Details);
        Assert.True(usage.Details.Count >= 3, $"Expected at least 3 details (burst, weekly, spark), got {usage.Details.Count}");

        // Verify burst window detail (primary_window)
        var burstDetail = usage.Details.FirstOrDefault(d =>
            d.QuotaBucketKind == WindowKind.Burst &&
            d.DetailType == ProviderUsageDetailType.QuotaWindow &&
            string.IsNullOrEmpty(d.ModelName));
        Assert.NotNull(burstDetail);
        Assert.NotNull(burstDetail!.PercentageValue);

        // primary_window used=42.5% → remaining=57.5%
        Assert.Equal(57.5, burstDetail.PercentageValue!.Value, precision: 1);
        Assert.Equal(PercentageValueSemantic.Remaining, burstDetail.PercentageSemantic);

        // Verify weekly window detail (secondary_window)
        var weeklyDetail = usage.Details.FirstOrDefault(d =>
            d.QuotaBucketKind == WindowKind.Rolling &&
            d.DetailType == ProviderUsageDetailType.QuotaWindow &&
            string.IsNullOrEmpty(d.ModelName));
        Assert.NotNull(weeklyDetail);
        Assert.NotNull(weeklyDetail!.PercentageValue);

        // secondary_window used=28% → remaining=72%
        Assert.Equal(72.0, weeklyDetail.PercentageValue!.Value, precision: 1);
        Assert.Equal(PercentageValueSemantic.Remaining, weeklyDetail.PercentageSemantic);

        // Verify spark detail exists (ModelSpecific kind)
        var sparkDetail = usage.Details.FirstOrDefault(d =>
            d.QuotaBucketKind == WindowKind.ModelSpecific &&
            d.DetailType == ProviderUsageDetailType.QuotaWindow);
        Assert.NotNull(sparkDetail);
        Assert.Contains("Spark", sparkDetail!.Name, StringComparison.OrdinalIgnoreCase);

        // Verify credits detail
        var creditsDetail = usage.Details.FirstOrDefault(d => d.DetailType == ProviderUsageDetailType.Credit);
        Assert.NotNull(creditsDetail);
        Assert.Contains("150", creditsDetail!.Description, StringComparison.Ordinal);

        // Verify model detail exists
        var modelDetail = usage.Details.FirstOrDefault(d => d.DetailType == ProviderUsageDetailType.Model);
        Assert.NotNull(modelDetail);
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
        var config = new ProviderConfig { ProviderId = "codex", ApiKey = "test-token" };

        var usages = (await provider.GetUsageAsync(config)).ToList();

        Assert.Single(usages);
        var usage = usages[0];
        Assert.True(usage.IsAvailable);
        Assert.Equal(85.0, usage.UsedPercent, precision: 1);

        // Should still have burst detail and model detail, but no weekly or spark
        Assert.NotNull(usage.Details);
        var weeklyDetails = usage.Details.Where(d => d.QuotaBucketKind == WindowKind.Rolling).ToList();
        Assert.Empty(weeklyDetails);
    }

    /// <summary>
    /// Claude Code provider: realistic OAuth usage response with five_hour, seven_day,
    /// seven_day_sonnet, and seven_day_opus windows.
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

        // Call the internal GetUsageFromOAuthAsync which is visible to tests
        var usage = await provider.GetUsageFromOAuthAsync("test-oauth-token");

        // Assert
        Assert.NotNull(usage);
        Assert.Equal("claude-code", usage!.ProviderId);
        Assert.True(usage.IsAvailable);

        // Main percent = max(five_hour=35, seven_day=48) = 48
        Assert.Equal(48.0, usage.UsedPercent, precision: 1);

        // Should contain details for all windows
        Assert.NotNull(usage.Details);
        Assert.True(usage.Details.Count >= 4, $"Expected at least 4 details, got {usage.Details.Count}");

        // Current Session (5-hour burst)
        var sessionDetail = usage.Details.FirstOrDefault(d =>
            d.QuotaBucketKind == WindowKind.Burst);
        Assert.NotNull(sessionDetail);
        Assert.Equal("Current Session", sessionDetail!.Name);
        Assert.NotNull(sessionDetail.PercentageValue);
        Assert.Equal(35.0, sessionDetail.PercentageValue!.Value, precision: 1);
        Assert.Equal(PercentageValueSemantic.Used, sessionDetail.PercentageSemantic);

        // Sonnet (7-day model-specific)
        var sonnetDetail = usage.Details.FirstOrDefault(d =>
            d.Name == "Sonnet");
        Assert.NotNull(sonnetDetail);
        Assert.Equal(WindowKind.ModelSpecific, sonnetDetail!.QuotaBucketKind);
        Assert.Equal(52.0, sonnetDetail.PercentageValue!.Value, precision: 1);

        // Opus (7-day model-specific)
        var opusDetail = usage.Details.FirstOrDefault(d =>
            d.Name == "Opus");
        Assert.NotNull(opusDetail);
        Assert.Equal(WindowKind.ModelSpecific, opusDetail!.QuotaBucketKind);
        Assert.Equal(10.0, opusDetail.PercentageValue!.Value, precision: 1);

        // All Models (7-day rolling)
        var allModelsDetail = usage.Details.FirstOrDefault(d =>
            d.QuotaBucketKind == WindowKind.Rolling);
        Assert.NotNull(allModelsDetail);
        Assert.Equal("All Models", allModelsDetail!.Name);
        Assert.Equal(48.0, allModelsDetail.PercentageValue!.Value, precision: 1);

        // Description should mention extra usage
        Assert.Contains("Extra usage enabled", usage.Description, StringComparison.Ordinal);
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

        var usage = await provider.GetUsageFromOAuthAsync("test-token");

        Assert.NotNull(usage);
        Assert.Equal(90.0, usage!.UsedPercent, precision: 1);

        // Should have at least the current session detail
        var sessionDetail = usage.Details?.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Burst);
        Assert.NotNull(sessionDetail);
        Assert.Equal(90.0, sessionDetail!.PercentageValue!.Value, precision: 1);

        // No rolling window detail since seven_day is null
        var rollingDetail = usage.Details?.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Rolling);
        Assert.Null(rollingDetail);
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
        var config = new ProviderConfig { ProviderId = "codex", ApiKey = "expired-token" };

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
