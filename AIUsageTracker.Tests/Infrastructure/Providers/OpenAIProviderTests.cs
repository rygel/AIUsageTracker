// <copyright file="OpenAIProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class OpenAIProviderTests : HttpProviderTestBase<OpenAIProvider>
{
    private static readonly string TestApiKeyStandard = "sk-" + Guid.NewGuid().ToString();
    private static readonly string TestApiKeyProject = "sk-proj-" + Guid.NewGuid().ToString();
    private static readonly string TestApiKeySession = Guid.NewGuid().ToString();
    private static readonly string TestApiKeyExpired = Guid.NewGuid().ToString();

    private readonly OpenAIProvider _provider;
    private readonly Mock<IProviderDiscoveryService> _discoveryService = new();

    public OpenAIProviderTests()
    {
        this._provider = new OpenAIProvider(this.HttpClient, this._discoveryService.Object, this.Logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_StandardApiKey_ReturnsConnectedStatusAsync()
    {
        // Arrange
        this.Config.ApiKey = TestApiKeyStandard;
        this.SetupHttpResponse("https://api.openai.com/v1/models", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"data\":[]}"),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("OpenAI (API)", usage.ProviderName);
        Assert.Equal("Connected (API Key)", usage.Description);
        Assert.Equal(200, usage.HttpStatus);
    }

    [Fact]
    public async Task GetUsageAsync_ProjectApiKey_ReturnsNotSupportedMessageAsync()
    {
        // Arrange
        this.Config.ApiKey = TestApiKeyProject;

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Project keys (sk-proj-...) not supported", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_NativeSession_ParsesQuotaCorrectlyAsync()
    {
        // Arrange
        this.Config.ApiKey = TestApiKeySession; // Not starting with sk-
        var responseData = new
        {
            plan_type = "plus",
            email = "user@example.com",
            rate_limit = new
            {
                primary_window = new
                {
                    used_percent = 45.5,
                    reset_after_seconds = 3600,
                },
                secondary_window = new
                {
                    used_percent = 10.0,
                    reset_after_seconds = 86400,
                },
            },
        };

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert — provider now emits flat cards: burst + weekly
        var usages = result.ToList();
        Assert.Equal(2, usages.Count);

        var burstCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Burst);
        Assert.True(burstCard.IsAvailable);
        Assert.Equal("user@example.com", burstCard.AccountName);
        Assert.Equal(45.5, burstCard.UsedPercent); // 5h burst: 45.5% used
        Assert.Equal(45.5, burstCard.RequestsUsed);
        Assert.Contains("Plan: plus", burstCard.Description, StringComparison.Ordinal);
        Assert.Equal("5-hour quota", burstCard.Name);
        Assert.Equal(54.5, burstCard.RemainingPercent, precision: 1); // 100 - 45.5 = 54.5% remaining
        Assert.Contains("Resets in", burstCard.Description, StringComparison.Ordinal);

        var weeklyCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Rolling);
        Assert.Equal("Weekly quota", weeklyCard.Name);
        Assert.Equal(90.0, weeklyCard.RemainingPercent, precision: 1); // 100 - 10 = 90% remaining
        Assert.Contains("Resets in", weeklyCard.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_LoadsSessionAuthFromDiscoveryServiceAsync()
    {
        // Arrange
        this._discoveryService.Setup(d => d.DiscoverAuthAsync(It.IsAny<ProviderAuthDiscoverySpec>()))
            .ReturnsAsync(new ProviderAuthData("session-from-mock", "acct-from-mock"));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("""
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 20,
                  "reset_after_seconds": 3600
                }
              }
            }
            """),
        });

        // Act
        var result = await this._provider.GetUsageAsync(new ProviderConfig { ProviderId = "openai" });

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("acct-from-mock", usage.AccountName);
        Assert.Equal("OpenCode Session", usage.AuthSource);
    }

    [Fact]
    public async Task GetUsageAsync_NativeSession_UsesConfiguredProfileRootForAccountIdentityAsync()
    {
        this.Config.ApiKey = CreateSessionJwtWithProfileName("OpenAI Profile User");
        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("""
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 20,
                  "reset_after_seconds": 3600
                }
              }
            }
            """),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("OpenAI Profile User", usage.AccountName);
    }

    [Fact]
    public async Task GetUsageAsync_InvalidSession_ReturnsUnavailableAsync()
    {
        // Arrange
        this.Config.ApiKey = TestApiKeyExpired;
        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(401, usage.HttpStatus);
        Assert.Contains("Session invalid", usage.Description, StringComparison.Ordinal);
    }

    private static string CreateSessionJwtWithProfileName(string name)
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncode(JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["https://api.openai.com/profile"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
            },
        }));

        return $"{header}.{payload}.";
    }

    [Fact]
    public async Task GetUsageAsync_NativeSession_BurstWindowJustReset_EmitsBurstDetailWith100RemainingAsync()
    {
        // Regression: when the 5h burst window just reset, the API may omit used_percent and
        // only return reset_after_seconds. The previous guard `if (used.HasValue)` would skip
        // the Burst detail entirely, preventing dual-bar rendering on the parent card.
        // With the fix, the Burst detail is emitted with 100% remaining (0% used).
        this.Config.ApiKey = TestApiKeySession;

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    // Burst window just reset — API omits used_percent, only reset timer present
                    primary_window = new { reset_after_seconds = 18000 },
                    secondary_window = new { used_percent = 30.0, reset_after_seconds = 604800 },
                },
            })),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        // Burst flat card must be present even though used_percent was absent in API response
        var burstCard = Assert.Single(result, u => u.WindowKind == WindowKind.Burst);
        Assert.True(burstCard.IsAvailable);
        Assert.Equal("5-hour quota", burstCard.Name);
        Assert.Equal(0.0, burstCard.UsedPercent); // burst just reset → 0% used
        Assert.Equal(100.0, burstCard.RemainingPercent); // 100% remaining

        // Rolling flat card must still be present
        var rollingCard = Assert.Single(result, u => u.WindowKind == WindowKind.Rolling);
        Assert.Equal(30.0, rollingCard.UsedPercent); // 30% used
        Assert.Equal(70.0, rollingCard.RemainingPercent); // 70% remaining
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
