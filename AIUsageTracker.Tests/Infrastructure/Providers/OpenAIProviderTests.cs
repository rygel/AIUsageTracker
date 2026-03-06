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
                },
                secondary_window = new
                {
                    used_percent = 10.0,
                    reset_after_seconds = 86400
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
        
        // Regression test for Dual Progress Bars
        Assert.NotNull(usage.Details);
        var primary = usage.Details.FirstOrDefault(d => d.WindowKind == WindowKind.Primary);
        var secondary = usage.Details.FirstOrDefault(d => d.WindowKind == WindowKind.Secondary);
        
        Assert.NotNull(primary);
        Assert.Equal("5-hour quota", primary.Name);
        Assert.Contains("46% used", primary.Used);

        Assert.NotNull(secondary);
        Assert.Equal("Weekly quota", secondary.Name);
        Assert.Contains("10% used", secondary.Used);
    }

    [Fact]
    public async Task GetUsageAsync_LoadsSessionAuthFromMetadataDefinedAuthFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"openai-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth.json");
        await File.WriteAllTextAsync(authPath, """
            {
              "openai": {
                "access": "session-from-file",
                "accountId": "acct-from-file"
              }
            }
            """);

        SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
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
                """)
        });

        var provider = new OpenAIProvider(HttpClient, Logger.Object, authPath);

        try
        {
            var result = await provider.GetUsageAsync(new ProviderConfig { ProviderId = "openai" });

            var usage = result.Single();
            Assert.True(usage.IsAvailable);
            Assert.Equal("acct-from-file", usage.AccountName);
            Assert.Equal("OpenCode Session", usage.AuthSource);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
