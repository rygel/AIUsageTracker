// <copyright file="MinimaxProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class MinimaxProviderTests : HttpProviderTestBase<MinimaxProvider>
{
    private const string TokenPlanEndpoint = "https://api.minimax.io/v1/token_plan/remains";
    private const string CodingPlanEndpoint = "https://api.minimax.io/v1/api/openplatform/coding_plan/remains";

    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly MinimaxProvider _provider;

    public MinimaxProviderTests()
    {
        this._provider = new MinimaxProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
        this.Config.ProviderId = MinimaxProvider.ChinaProviderId;
    }

    [Fact]
    public async Task GetUsageAsync_Always_ReturnsTwoCardsAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 35,
                        "current_weekly_remaining_percent": 49
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Single(result, u => u.WindowKind == WindowKind.Burst);
        Assert.Single(result, u => u.WindowKind == WindowKind.Rolling);
    }

    [Fact]
    public async Task GetUsageAsync_ModerateUsage_ReturnsUsedPercentageAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 65,
                        "current_weekly_remaining_percent": 70
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.True(burst.IsQuotaBased);
        Assert.Equal(35, burst.UsedPercent);
        Assert.Equal(100, burst.RequestsAvailable);
    }

    [Fact]
    public async Task GetUsageAsync_HighUsage_ReturnsCorrectUsedPercentAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 15,
                        "current_weekly_remaining_percent": 10
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(85, burst.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_AtCapacity_ReturnsFullUsedAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 0,
                        "current_weekly_remaining_percent": 0
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(100, burst.UsedPercent);
        Assert.Equal(100, burst.RequestsUsed);

        var weekly = result.Single(u => u.WindowKind == WindowKind.Rolling);
        Assert.Equal(100, weekly.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_MinimalUsage_ReturnsLowUsedPercentAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 98,
                        "current_weekly_remaining_percent": 99
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(2, burst.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_ZeroUsage_ReturnsZeroUsedPercentAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 100,
                        "current_weekly_remaining_percent": 100
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(0, burst.UsedPercent);
        Assert.Equal(0, burst.RequestsUsed);
    }

    [Fact]
    public async Task GetUsageAsync_OverLimit_ClampsToMaxUsedAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 120,
                        "current_weekly_remaining_percent": 150
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(0, burst.UsedPercent);
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
    public async Task GetUsageAsync_WithChinaProviderId_UsesTokenPlanEndpointAsync()
    {
        this.Config.ProviderId = MinimaxProvider.ChinaProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 50
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, TokenPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal("minimax", burst.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("MiniMax.io", burst.ProviderName);
    }

    [Fact]
    public async Task GetUsageAsync_WithInternationalProviderId_UsesTokenPlanEndpointAsync()
    {
        this.Config.ProviderId = MinimaxProvider.InternationalProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 50
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, TokenPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal("minimax-io", burst.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("MiniMax.io", burst.ProviderName);
    }

    [Fact]
    public async Task GetUsageAsync_WithLegacyProviderId_UsesTokenPlanEndpointAsync()
    {
        this.Config.ProviderId = MinimaxProvider.InternationalLegacyProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 50
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, TokenPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal("minimax-global", burst.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("MiniMax.io", burst.ProviderName);
    }

    [Fact]
    public async Task GetUsageAsync_WithCustomBaseUrl_UsesCustomEndpointAsync()
    {
        var customUrl = "https://custom.minimax.example.com/v1/token_plan/remains";
        this.Config.BaseUrl = customUrl;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 50
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, customUrl);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.True(burst.IsAvailable);
        Assert.Equal(50, burst.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_NoApiKey_ReturnsUnavailableAsync()
    {
        this.Config.ApiKey = string.Empty;

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("API Key not found", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_ServerError_ReturnsUnavailableAsync()
    {
        this.SetupResponse(HttpStatusCode.InternalServerError, """{"error": "internal_error"}""");

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("InternalServerError", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_Unauthorized_ReturnsUnavailableAsync()
    {
        this.SetupResponse(HttpStatusCode.Unauthorized, """{"error": "invalid_api_key"}""");

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Unauthorized", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_InvalidJson_ReturnsUnavailableAsync()
    {
        this.SetupResponse(HttpStatusCode.OK, "not valid json");

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Failed to parse", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_MissingModelRemains_ReturnsUnavailableAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" }
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Invalid MiniMax response", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_RateLimited_ReturnsUnavailableAsync()
    {
        this.SetupResponse(HttpStatusCode.TooManyRequests, """{"error": "rate_limited"}""");

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("TooManyRequests", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_ZeroRemainingPercent_ReturnsTwoCardsBothExhaustedAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 0,
                        "current_weekly_remaining_percent": 0
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, u => Assert.Equal(100, u.UsedPercent));
    }

    [Fact]
    public async Task GetUsageAsync_DescriptionFormat_IsCorrectAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 50
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Contains("remaining", burst.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_IsNotCurrencyUsage_ForTokenBasedProviderAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 50
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.False(burst.IsCurrencyUsage);
    }

    [Fact]
    public async Task GetUsageAsync_PlanType_IsCodingAsync()
    {
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "general",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 50
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(PlanType.Coding, burst.PlanType);
    }

    [Fact]
    public async Task GetUsageAsync_WithCodingPlanProviderId_UsesCodingPlanEndpointAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-Text-01",
                        "current_interval_remaining_percent": 70,
                        "current_weekly_remaining_percent": 80
                    }
                ]
            }
            """;

        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        Assert.Equal(2, result.Count);
        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        var weekly = result.Single(u => u.WindowKind == WindowKind.Rolling);

        Assert.Equal(30, burst.UsedPercent);
        Assert.Equal(100, burst.RequestsAvailable);
        Assert.Equal("minimax-coding-plan", burst.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Minimax.io Coding Plan", burst.ProviderName);

        Assert.Equal(20, weekly.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_ApiError_ReturnsUnavailableAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        this.SetupResponse(HttpStatusCode.Unauthorized, """{"error":"invalid_key"}""", CodingPlanEndpoint);

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Unauthorized", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_NonZeroStatusCode_ReturnsUnavailableAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """{"base_resp": {"status_code": 1004, "status_msg": "invalid api key"}}""";
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("invalid api key", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_AtCapacity_Returns100PercentAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-Text-01",
                        "current_interval_remaining_percent": 0,
                        "current_weekly_remaining_percent": 0
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, u => Assert.Equal(100, u.UsedPercent));
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_DisplayName_IsMiniMaxCodingPlanAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-Text-01",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 50
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = await this._provider.GetUsageAsync(this.Config);

        Assert.All(result, u => Assert.Equal("Minimax.io Coding Plan", u.ProviderName));
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_PrefersTextGenerationModelAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "Image Generation",
                        "current_interval_remaining_percent": 75,
                        "current_weekly_remaining_percent": 90
                    },
                    {
                        "model_name": "Text Generation",
                        "current_interval_remaining_percent": 80,
                        "current_weekly_remaining_percent": 90
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(20, burst.UsedPercent);
        Assert.Equal(100, burst.RequestsAvailable);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_DoesNotUseSearchModel_WhenTextGenerationExistsAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "Text Search",
                        "current_interval_remaining_percent": 50,
                        "current_weekly_remaining_percent": 10
                    },
                    {
                        "model_name": "Text Generation",
                        "current_interval_remaining_percent": 90,
                        "current_weekly_remaining_percent": 90
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, usage => Assert.DoesNotContain("Search", usage.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, usage => string.Equals(usage.Name, "5h", StringComparison.Ordinal));
        Assert.Contains(result, usage => string.Equals(usage.Name, "Weekly", StringComparison.Ordinal));

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(10, burst.UsedPercent);
        Assert.Equal(100, burst.RequestsAvailable);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_WithMiniMaxMModel_SelectsCodingModelAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-M*",
                        "current_interval_remaining_percent": 99,
                        "end_time": 1776783600000,
                        "current_weekly_remaining_percent": 50,
                        "weekly_end_time": 1777248000000
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, usage => Assert.True(usage.IsAvailable));

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        var weekly = result.Single(u => u.WindowKind == WindowKind.Rolling);
        Assert.Equal("5h", burst.Name);
        Assert.Equal("Weekly", weekly.Name);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_EndTimeMilliseconds_MapsToUtcResetTimeAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        const long burstResetMs = 1776783600000;
        const long weeklyResetMs = 1777248000000;
        var responseJson = $$"""
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-M*",
                        "current_interval_remaining_percent": 99,
                        "end_time": {{burstResetMs}},
                        "current_weekly_remaining_percent": 50,
                        "weekly_end_time": {{weeklyResetMs}}
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        var weekly = result.Single(u => u.WindowKind == WindowKind.Rolling);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(burstResetMs).UtcDateTime, burst.NextResetTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(weeklyResetMs).UtcDateTime, weekly.NextResetTime);
        Assert.Equal(DateTimeKind.Utc, burst.NextResetTime!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, weekly.NextResetTime!.Value.Kind);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_UsesStartAndEndTimeForPeriodDurationAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        const long burstStartMs = 1776762000000;
        const long burstEndMs = 1776783600000;
        const long weeklyStartMs = 1776600000000;
        const long weeklyEndMs = 1777248000000;

        var responseJson = $$"""
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "MiniMax-M*",
                        "current_interval_remaining_percent": 90,
                        "start_time": {{burstStartMs}},
                        "end_time": {{burstEndMs}},
                        "current_weekly_remaining_percent": 50,
                        "weekly_start_time": {{weeklyStartMs}},
                        "weekly_end_time": {{weeklyEndMs}}
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = (await this._provider.GetUsageAsync(this.Config)).ToList();

        var burst = result.Single(u => u.WindowKind == WindowKind.Burst);
        var weekly = result.Single(u => u.WindowKind == WindowKind.Rolling);
        Assert.Equal(TimeSpan.FromHours(6), burst.PeriodDuration);
        Assert.Equal(TimeSpan.FromHours(180), weekly.PeriodDuration);
    }

    [Fact]
    public async Task GetUsageAsync_CodingPlan_WithoutTextGenerationModel_ReturnsUnavailableAsync()
    {
        this.Config.ProviderId = MinimaxProvider.CodingPlanProviderId;
        var responseJson = """
            {
                "base_resp": { "status_code": 0, "status_msg": "success" },
                "model_remains": [
                    {
                        "model_name": "Image Generation",
                        "current_interval_remaining_percent": 75,
                        "current_weekly_remaining_percent": 90
                    }
                ]
            }
            """;
        this.SetupResponse(HttpStatusCode.OK, responseJson, CodingPlanEndpoint);

        var result = await this._provider.GetUsageAsync(this.Config);

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
        url ??= TokenPlanEndpoint;
        this.SetupHttpResponse(
            r => string.Equals(r.RequestUri?.ToString(), url, StringComparison.Ordinal),
            new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
