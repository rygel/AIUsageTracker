using System.Net;
using System.Text.Json;
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

    public OpenAIProviderTests()
    {
        _provider = new OpenAIProvider(HttpClient, Logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_StandardApiKey_ReturnsConnectedStatus()
    {
        // Arrange
        Config.ApiKey = "sk-test-key";
        SetupHttpResponse("https://api.openai.com/v1/models", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"data\":[]}")
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("OpenAI", usage.ProviderName);
        Assert.Equal("Connected (API Key)", usage.Description);
        Assert.Equal(200, usage.HttpStatus);
    }

    [Fact]
    public async Task GetUsageAsync_ProjectApiKey_ReturnsNotSupportedMessage()
    {
        // Arrange
        Config.ApiKey = "sk-proj-test-key";

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Project keys (sk-proj-...) not supported", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_NativeSession_ParsesQuotaCorrectly()
    {
        // Arrange
        Config.ApiKey = "session-token"; // Not starting with sk-
        var responseData = new
        {
            plan_type = "plus",
            email = "user@example.com",
            rate_limit = new
            {
                primary_window = new
                {
                    used_percent = 45.5,
                    reset_after_seconds = 3600
                }
            }
        };

        SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData))
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("user@example.com", usage.AccountName);
        Assert.Equal(54.5, usage.RequestsPercentage); // 100 - 45.5
        Assert.Equal(45.5, usage.RequestsUsed);
        Assert.Contains("Plan: plus", usage.Description);
        
        var detail = usage.Details!.First();
        Assert.Equal("5-hour quota", detail.Name);
        Assert.Equal("46% used", detail.Used);
    }

    [Fact]
    public async Task GetUsageAsync_InvalidSession_ReturnsUnavailable()
    {
        // Arrange
        Config.ApiKey = "expired-session";
        SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(401, usage.HttpStatus);
        Assert.Contains("Session invalid", usage.Description);
    }
}
