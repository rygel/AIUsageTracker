using System.Net;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

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
        Assert.Contains("auth token not found", usage.Description, StringComparison.OrdinalIgnoreCase);
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
                    model_name = "OpenAI-Codex-Live",
                    plan_type = "plus",
                    rate_limit = new
                    {
                        primary_window = new { used_percent = 25, reset_after_seconds = 1200 },
                        secondary_window = new { used_percent = 10, reset_after_seconds = 600 }
                    },
                    additional_rate_limits = new[]
                    {
                        new
                        {
                            limit_name = "spark-plan-window",
                            model_name = "GPT-5.3-Codex-Spark",
                            rate_limit = new
                            {
                                primary_window = new { used_percent = 40, reset_after_seconds = 3600 }
                            }
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
            var allUsages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();
            var usage = allUsages.Single(u => u.ProviderId == "codex");
            var sparkUsage = allUsages.Single(u => u.ProviderId == "codex.spark");

            // Assert
            Assert.Equal(2, allUsages.Count);
            Assert.True(usage.IsAvailable);
            Assert.Equal("OpenAI (Codex)", usage.ProviderName);
            Assert.Equal("user@example.com", usage.AccountName);
            Assert.Equal(75.0, usage.RequestsPercentage);
            Assert.Contains("Plan: plus", usage.Description);
            Assert.Contains("Spark", usage.Description);
            Assert.Contains(usage.Details!, d => d.DetailType == ProviderUsageDetailType.Model && d.Name == "OpenAI-Codex-Live");
            Assert.Contains(usage.Details!, d => d.Name == "5-hour quota");
            Assert.Contains(usage.Details!, d => d.Name == "5-hour quota" && d.NextResetTime.HasValue);
            Assert.Contains(usage.Details!, d => d.Name == "Weekly quota" && d.NextResetTime.HasValue);
            Assert.Contains(usage.Details!, d => d.Name.StartsWith("Spark", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(usage.Details!, d => d.Name == "Credits" && d.Used == "7.50");
            Assert.True(sparkUsage.IsAvailable);
            Assert.Equal("GPT-5.3-Codex-Spark", sparkUsage.ProviderName);
            Assert.Equal("user@example.com", sparkUsage.AccountName);
            Assert.Equal(60.0, sparkUsage.RequestsPercentage);
            Assert.Contains("Spark", sparkUsage.Description);
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
            var allUsages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();
            var usage = allUsages.Single(u => u.ProviderId == "codex");

            // Assert
            Assert.True(usage.IsAvailable);
            Assert.Equal("snapshot@example.com", usage.AccountName);
            Assert.Equal(52.0, usage.RequestsPercentage);
            Assert.Contains("Plan: plus", usage.Description);
            Assert.NotNull(usage.NextResetTime);
            Assert.Contains(usage.Details!, d => d.DetailType == ProviderUsageDetailType.Model && d.Name == "OpenAI (Codex)");
            Assert.Contains(usage.Details!, d => d.Name == "5-hour quota" && d.Used.Contains("48% used"));
            Assert.Contains(usage.Details!, d => d.Name == "5-hour quota" && d.NextResetTime.HasValue);
            Assert.Contains(usage.Details!, d => d.Name == "Weekly quota" && d.NextResetTime.HasValue);
            Assert.Contains(usage.Details!, d => d.Name == "Credits" && d.Used == "0.00");
            Assert.DoesNotContain(usage.Details!, d => d.Name == "OpenAI (GPT-5.3-Codex-Spark)");
            Assert.DoesNotContain(allUsages, u => u.ProviderId == "codex.spark");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetUsageAsync_WhenJwtEmailMissing_UsesAccountIdAsIdentity()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"codex-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth.json");
        var nonJwtToken = "not-a-jwt-token";
        var accountId = "acct_fallback_456";

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = nonJwtToken,
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
                    request.Headers.Authorization.Parameter == nonJwtToken),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    plan_type = "plus",
                    rate_limit = new
                    {
                        primary_window = new { used_percent = 10, reset_after_seconds = 600 }
                    }
                }))
            });

        var provider = new CodexProvider(_httpClient, _logger.Object, authPath);

        try
        {
            // Act
            var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" }))
                .Single(u => u.ProviderId == "codex");

            // Assert
            Assert.Equal(accountId, usage.AccountName);
            Assert.True(usage.IsAvailable);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetUsageAsync_IdentityFallsBackToIdToken_WhenAccessTokenLacksEmail()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"codex-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth.json");
        var accessTokenWithoutEmail = CreateJwtWithoutIdentity();
        var idTokenWithEmail = CreateJwt("id-token@example.com", "plus");
        var accountId = "acct_id_token";

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = accessTokenWithoutEmail,
                id_token = idTokenWithEmail,
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
                    request.Headers.Authorization.Parameter == accessTokenWithoutEmail),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    plan_type = "plus",
                    rate_limit = new
                    {
                        primary_window = new { used_percent = 20, reset_after_seconds = 1200 }
                    }
                }))
            });

        var provider = new CodexProvider(_httpClient, _logger.Object, authPath);

        try
        {
            // Act
            var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" }))
                .Single(u => u.ProviderId == "codex");

            // Assert
            Assert.Equal("id-token@example.com", usage.AccountName);
            Assert.True(usage.IsAvailable);
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

    private static string CreateJwtWithoutIdentity()
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [ "https://api.openai.com/auth" ] = new Dictionary<string, object?>
            {
                ["chatgpt_plan_type"] = "plus",
                ["chatgpt_user_id"] = "user_without_email"
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

