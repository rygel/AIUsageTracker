using System.Net;
using System.Text;
using System.Text.Json;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class CodexProviderTests
{
    private readonly Mock<HttpMessageHandler> _messageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<CodexProvider>> _logger;

    public CodexProviderTests()
    {
        _messageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_messageHandler.Object);
        _logger = new Mock<ILogger<CodexProvider>>();
    }

    [Fact]
    public async Task GetUsageAsync_AuthFileMissing_ReturnsUnavailable()
    {
        // Arrange
        var missingAuthPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}", "auth.json");
        var provider = new CodexProvider(_httpClient, _logger.Object, missingAuthPath);

        // Act
        var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();

        // Assert
        Assert.False(usage.IsAvailable);
        Assert.Contains("auth not found", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_NativeAuthAndUsageResponse_ReturnsParsedUsage()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"codex-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");
        var accountId = "acct_123";

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
                account_id = accountId
            }
        }));

        _messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Get &&
                    request.RequestUri!.ToString() == "https://chatgpt.com/backend-api/wham/usage" &&
                    request.Headers.Authorization != null &&
                    request.Headers.Authorization.Scheme == "Bearer" &&
                    request.Headers.Authorization.Parameter == token &&
                    request.Headers.Contains("ChatGPT-Account-Id") &&
                    request.Headers.GetValues("ChatGPT-Account-Id").Contains(accountId)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    plan_type = "plus",
                    rate_limit = new
                    {
                        primary_window = new { used_percent = 25, reset_after_seconds = 1200 },
                        secondary_window = new { used_percent = 10, reset_after_seconds = 600 },
                        spark_weekly_window = new
                        {
                            primary_window = new { used_percent = 40, reset_after_seconds = 3600 }
                        }
                    },
                    credits = new
                    {
                        balance = 7.5,
                        unlimited = false
                    }
                }))
            });

        var provider = new CodexProvider(_httpClient, _logger.Object, authPath);

        try
        {
            // Act
            var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();

            // Assert
            Assert.True(usage.IsAvailable);
            Assert.Equal("Codex", usage.ProviderName);
            Assert.Equal("user@example.com", usage.AccountName);
            Assert.Equal(75.0, usage.RequestsPercentage);
            Assert.Contains("Plan: plus", usage.Description);
            Assert.Contains("Spark", usage.Description);
            Assert.Contains(usage.Details!, d => d.Name == "Primary Window");
            Assert.Contains(usage.Details!, d => d.Name.StartsWith("Spark", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(usage.Details!, d => d.Name == "Credits" && d.Used == "7.50");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetUsageAsync_WhamSnapshot_ParsesContractFields()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"codex-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("snapshot@example.com", "pro");
        var accountId = "acct_snapshot";
        var snapshotJson = LoadFixture("codex_wham_usage.snapshot.json");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
                account_id = accountId
            }
        }));

        _messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Get &&
                    request.RequestUri!.ToString() == "https://chatgpt.com/backend-api/wham/usage" &&
                    request.Headers.Authorization != null &&
                    request.Headers.Authorization.Parameter == token),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(snapshotJson)
            });

        var provider = new CodexProvider(_httpClient, _logger.Object, authPath);

        try
        {
            // Act
            var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();

            // Assert
            Assert.True(usage.IsAvailable);
            Assert.Equal("snapshot@example.com", usage.AccountName);
            Assert.Equal(37.5, usage.RequestsPercentage);
            Assert.Contains("Plan: pro", usage.Description);
            Assert.Contains("Spark", usage.Description);
            Assert.NotNull(usage.NextResetTime);
            Assert.Contains(usage.Details!, d => d.Name == "Primary Window" && d.Used == "62% used");
            Assert.Contains(usage.Details!, d => d.Name.StartsWith("Spark", StringComparison.OrdinalIgnoreCase) && d.Used == "40% used");
            Assert.Contains(usage.Details!, d => d.Name == "Credits" && d.Used == "12.75");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateJwt(string email, string planType)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [ "https://api.openai.com/profile" ] = new Dictionary<string, object?>
            {
                ["email"] = email,
                ["email_verified"] = true
            },
            [ "https://api.openai.com/auth" ] = new Dictionary<string, object?>
            {
                ["chatgpt_plan_type"] = planType,
                ["chatgpt_user_id"] = "user_123"
            },
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        return $"{Base64UrlEncode(headerJson)}.{Base64UrlEncode(payloadJson)}.sig";
    }

    private static string Base64UrlEncode(string value)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string LoadFixture(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "Providers", fileName);
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");
        return File.ReadAllText(fixturePath);
    }
}
