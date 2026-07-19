// <copyright file="CodexProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
#pragma warning disable CS0618

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class CodexProviderTests : HttpProviderTestBase<CodexProvider>
{
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
            var allUsages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).OfType<ModelScopedProviderUsage>().ToList();

            // Assert: flat cards — burst card and weekly card emitted separately
            var burstUsage = Assert.Single(allUsages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "burst", StringComparison.Ordinal));
            var weeklyUsage = Assert.Single(allUsages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "weekly", StringComparison.Ordinal));

            Assert.True(burstUsage.IsAvailable);
            Assert.Equal("OpenAI (Codex)", burstUsage.ProviderName);
            Assert.Equal("user@example.com", burstUsage.AccountName);
            Assert.Equal(25.0, burstUsage.UsedPercent); // 25% used (75% remaining)
            Assert.Equal(WindowKind.Burst, burstUsage.WindowKind);
            Assert.Equal(WindowKind.Rolling, weeklyUsage.WindowKind);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_UsesConfiguredProfileRootForAccountIdentityAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-profile-name");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwtWithProfileName("Codex Profile User");

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
                model_name = "OpenAI-Codex-Live",
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 25, reset_after_seconds = 1200 },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();
            Assert.True(usage.IsAvailable);
            Assert.Equal("Codex Profile User", usage.AccountName);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_PrefersAbsoluteResetAtEpoch_OverRelativeResetAfterSecondsAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-reset-at-epoch");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new { access_token = token },
        }));

        const long primaryResetAt = 1800000000L;
        const long secondaryResetAt = 1800604800L;

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 25, reset_after_seconds = 1200, reset_at = primaryResetAt },
                    secondary_window = new { used_percent = 10, reset_after_seconds = 600, reset_at = secondaryResetAt },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).OfType<ModelScopedProviderUsage>().ToList();

            var burst = Assert.Single(usages, u => string.Equals(u.CardId, "burst", StringComparison.Ordinal));
            var expectedBurst = DateTimeOffset.FromUnixTimeSeconds(primaryResetAt).LocalDateTime;
            Assert.Equal(expectedBurst, burst.NextResetTime);

            var weekly = Assert.Single(usages, u => string.Equals(u.CardId, "weekly", StringComparison.Ordinal));
            var expectedWeekly = DateTimeOffset.FromUnixTimeSeconds(secondaryResetAt).LocalDateTime;
            Assert.Equal(expectedWeekly, weekly.NextResetTime);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_FallsBackToResetAfterSeconds_WhenResetAtAbsentAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-reset-fallback");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new { access_token = token },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 25, reset_after_seconds = 3600 },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).OfType<ModelScopedProviderUsage>().ToList();

            var burst = Assert.Single(usages, u => string.Equals(u.CardId, "burst", StringComparison.Ordinal));
            Assert.NotNull(burst.NextResetTime);
            var expectedEarliest = DateTime.Now.AddSeconds(3550);
            var expectedLatest = DateTime.Now.AddSeconds(3650);
            Assert.InRange(burst.NextResetTime!.Value, expectedEarliest, expectedLatest);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_ResetCredits_PropagatesSortedExpirationDatesAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-reset-credit-expirations");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
                account_id = "acct_123",
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
                    primary_window = new
                    {
                        used_percent = 29,
                        limit_window_seconds = 604800,
                        reset_at = 1784950041,
                    },
                },
                rate_limit_reset_credits = new
                {
                    available_count = 3,
                },
            })),
        });
        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/rate-limit-reset-credits", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(LoadFixture("codex_rate_limit_reset_credits.snapshot.json")),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usage = Assert.Single(
                (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" }))
                    .OfType<ModelScopedProviderUsage>());

            Assert.Equal(3, usage.ResetCreditsAvailable);
            Assert.Equal(
                new[]
                {
                    DateTime.Parse("2026-07-26T22:55:46.894538Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                    DateTime.Parse("2026-07-31T19:47:35.978539Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                    DateTime.Parse("2026-08-12T17:25:15.03266Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                },
                usage.ResetCreditExpirationsUtc);
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

    private static string CreateJwtWithProfileName(string name)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["https://api.openai.com/profile"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
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
