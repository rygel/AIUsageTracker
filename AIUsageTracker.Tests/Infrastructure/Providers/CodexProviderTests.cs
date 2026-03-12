// <copyright file="CodexProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class CodexProviderTests : HttpProviderTestBase<CodexProvider>
{
    [Fact]
    public void Constructor_WithoutExplicitAuthPath_KeepsCandidateSearchEnabled()
    {
        var provider = new CodexProvider(this.HttpClient, this.Logger.Object);
        var authFilePathField = typeof(CodexProvider).GetField("_authFilePath", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(authFilePathField);
        Assert.Null(authFilePathField!.GetValue(provider));
    }

    [Fact]
    public async Task GetUsageAsync_AuthFileMissing_ReturnsUnavailableAsync()
    {
        // Arrange
        var missingAuthPath = TestTempPaths.CreateFilePath("codex-test-missing-auth", "auth.json");
        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, missingAuthPath);

        // Act
        var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();

        // Assert
        Assert.False(usage.IsAvailable);
        Assert.Contains("auth token not found", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_MissingAccessToken_PreservesIdentityFromIdTokenAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-id-token-only");
        var authPath = Path.Combine(tempDir, "auth.json");
        var idToken = CreateJwt("codex-user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                id_token = idToken,
            },
        }));

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();
            Assert.False(usage.IsAvailable);
            Assert.Equal("codex-user@example.com", usage.AccountName);
            Assert.Contains("auth token not found", usage.Description, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_NativeAuthAndUsageResponse_ReturnsParsedUsageAsync()
    {
        // Arrange
        var tempDir = TestTempPaths.CreateDirectory("codex-test");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");
        var accountId = "acct_123";

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
                account_id = accountId,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model_name = "OpenAI-Codex-Live",
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 25, reset_after_seconds = 1200 },
                    secondary_window = new { used_percent = 10, reset_after_seconds = 600 },
                },
                credits = new
                {
                    balance = 7.5,
                    unlimited = false,
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            // Act
            var allUsages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();
            var usage = allUsages.Single(u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));

            // Assert
            Assert.True(usage.IsAvailable);
            Assert.Equal("OpenAI (Codex)", usage.ProviderName);
            Assert.Equal("user@example.com", usage.AccountName);
            Assert.Equal(75.0, usage.RequestsPercentage);
            Assert.NotNull(usage.Details);
            Assert.Contains(usage.Details, d => d.WindowKind == WindowKind.Primary);
            Assert.Contains(usage.Details, d => d.WindowKind == WindowKind.Secondary);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_PrefersSparkSpecificAdditionalRateLimit_WhenMultipleCandidatesExist()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-window");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 30, reset_after_seconds = 1200 },
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "gpt-5",
                        model_name = "gpt-5",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 0, reset_after_seconds = 2000 },
                        },
                    },
                    new
                    {
                        limit_name = "spark-window",
                        model_name = "gpt-5.3-codex-spark",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 40, reset_after_seconds = 1800 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();
            var spark = Assert.Single(usages.Where(usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal)));
            Assert.Equal(60, spark.RequestsPercentage);
            Assert.Equal(40, spark.RequestsUsed);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_ParsesSparkPrimaryAndSecondaryWindows()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-secondary-window");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 20, reset_after_seconds = 1200 },
                    secondary_window = new { used_percent = 10, reset_after_seconds = 6000 },
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "GPT-5.3-Codex-Spark",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 40, reset_after_seconds = 1800 },
                            secondary_window = new { used_percent = 75, reset_after_seconds = 36000 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();
            var spark = Assert.Single(usages.Where(usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal)));

            Assert.Equal(60, spark.RequestsPercentage);
            Assert.Equal(40, spark.RequestsUsed);
            Assert.NotNull(spark.Details);
            Assert.Contains(spark.Details!, detail => detail.WindowKind == WindowKind.Primary && detail.DetailType == ProviderUsageDetailType.QuotaWindow);
            Assert.Contains(spark.Details!, detail => detail.WindowKind == WindowKind.Secondary && detail.DetailType == ProviderUsageDetailType.QuotaWindow);
            Assert.Contains("Weekly: 75% used", spark.Description, StringComparison.Ordinal);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    private static string CreateJwt(string email, string planType)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["https://api.openai.com/profile"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["email"] = email,
            },
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        });

        return $"{Base64UrlEncode(headerJson)}.{Base64UrlEncode(payloadJson)}.sig";
    }

    private static string Base64UrlEncode(string value)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
