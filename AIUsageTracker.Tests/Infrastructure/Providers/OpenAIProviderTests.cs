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
    private readonly OpenAIProvider _provider;
    private readonly Mock<IProviderDiscoveryService> _discoveryService = new();

    public OpenAIProviderTests()
    {
        this._provider = new OpenAIProvider(this.ResilientHttpClient.Object, this._discoveryService.Object, this.Logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_StandardApiKey_ReturnsConnectedStatusAsync()
    {
        // Arrange
        this.Config.ApiKey = "sk-test-key";
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
        this.Config.ApiKey = "sk-proj-test-key";

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
        this.Config.ApiKey = "session-token"; // Not starting with sk-
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

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("user@example.com", usage.AccountName);
        Assert.Equal(54.5, usage.RequestsPercentage); // 100 - 45.5
        Assert.Equal(45.5, usage.RequestsUsed);
        Assert.Contains("Plan: plus", usage.Description, StringComparison.Ordinal);

        // Regression test for Dual Progress Bars
        Assert.NotNull(usage.Details);
        var primary = usage.Details.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Primary);
        var secondary = usage.Details.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Secondary);

        Assert.NotNull(primary);
        Assert.Equal("5-hour quota", primary.Name);
        Assert.Equal(54.5, primary.PercentageValue);
        Assert.Equal(PercentageValueSemantic.Remaining, primary.PercentageSemantic);
        Assert.Contains("remaining", primary.Used, StringComparison.Ordinal);

        Assert.NotNull(secondary);
        Assert.Equal("Weekly quota", secondary.Name);
        Assert.Equal(90.0, secondary.PercentageValue);
        Assert.Equal(PercentageValueSemantic.Remaining, secondary.PercentageSemantic);
        Assert.Contains("remaining", secondary.Used, StringComparison.Ordinal);
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
        this.Config.ApiKey = "expired-session";
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

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
