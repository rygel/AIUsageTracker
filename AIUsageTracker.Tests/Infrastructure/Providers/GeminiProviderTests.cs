using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class GeminiProviderTests : HttpProviderTestBase<GeminiProvider>
{
    private readonly GeminiProvider _provider;

    public GeminiProviderTests()
    {
        _provider = new GeminiProvider(HttpClient, Logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesBucketsCorrectly()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"gemini-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");
        
        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" }
            }
        }));

        var provider = new GeminiProvider(HttpClient, Logger.Object, accountsPath, null);

        SetupHttpResponse("https://oauth2.googleapis.com/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"access_token\":\"at\"}")
        });

        var quotaResponse = new
        {
            buckets = new[]
            {
                new { remainingFraction = 0.8, resetTime = "2026-03-10T12:00:00Z" }
            }
        };

        SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaResponse))
        });

        // Act
        var result = await provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("user@example.com", usage.AccountName);
        Assert.Contains("80", usage.RequestsPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("20", usage.RequestsUsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("80", usage.Description);
        
        Directory.Delete(tempDir, recursive: true);
    }
}