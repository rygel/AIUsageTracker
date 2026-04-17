// <copyright file="GeminiProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class GeminiProviderTests : HttpProviderTestBase<GeminiProvider>
{
    private readonly GeminiProvider _provider;

    public GeminiProviderTests()
    {
        this._provider = new GeminiProvider(this.HttpClient, this.Logger.Object);
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesBucketsCorrectlyAsync()
    {
        // Arrange
        var tempDir = TestTempPaths.CreateDirectory("gemini-test");
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");

        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" },
            },
        }));

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, Path.Combine(tempDir, "oauth_creds_override.json"));

        this.SetupHttpResponse("https://oauth2.googleapis.com/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"access_token\":\"at\"}"),
        });

        var quotaResponse = new
        {
            buckets = new[]
            {
                new { modelId = "gemini-2.5-flash", remainingFraction = 0.8, resetTime = "2026-03-10T12:00:00Z" },
            },
        };

        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaResponse)),
        });

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert — Gemini emits one flat model card per bucket with a modelId
        var usage = Assert.Single(
            result,
            item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.True(usage.IsAvailable);
        Assert.Equal("user@example.com", usage.AccountName);
        Assert.Contains("20", usage.UsedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal); // 20% used (80% remaining)
        Assert.Equal("gemini-2.5-flash", usage.ModelName);
        Assert.Contains("80", usage.Description, StringComparison.Ordinal); // "80.0% remaining"

        TestTempPaths.CleanupPath(tempDir);
    }

    [Fact]
    public async Task GetUsageAsync_UsesGeminiCliFallback_WhenAntigravityAccountsMissingAsync()
    {
        // Arrange
        var tempDir = TestTempPaths.CreateDirectory("gemini-fallback-test");
        var missingAccountsPath = Path.Combine(tempDir, "missing-antigravity-accounts.json");
        var oauthCredsPath = Path.Combine(tempDir, "oauth_creds.json");
        var projectsPath = Path.Combine(tempDir, "projects.json");
        var googleAccountsPath = Path.Combine(tempDir, "google_accounts.json");

        const string email = "fallback@example.com";
        var idToken = this.CreateUnsignedJwt(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["aud"] = "681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com",
            ["email"] = email,
        });

        await File.WriteAllTextAsync(oauthCredsPath, JsonSerializer.Serialize(new
        {
            refresh_token = "rt-fallback",
            id_token = idToken,
        }));
        await File.WriteAllTextAsync(googleAccountsPath, JsonSerializer.Serialize(new
        {
            active = "active@example.com",
            old = Array.Empty<string>(),
        }));

        var repoPath = @"c:\develop\claude\opencode-tracker";
        await File.WriteAllTextAsync(projectsPath, JsonSerializer.Serialize(new
        {
            projects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [repoPath] = "project-from-fallback",
            },
        }));

        var provider = new GeminiProvider(
            this.HttpClient,
            this.Logger.Object,
            missingAccountsPath,
            oauthCredsPath,
            tempDir,
            repoPath);

        this.SetupHttpResponse("https://oauth2.googleapis.com/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"access_token\":\"at-fallback\"}"),
        });
        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"buckets\":[{\"modelId\":\"gemini-2.5-flash\",\"remainingFraction\":0.65,\"resetTime\":\"2026-03-10T12:00:00Z\"}]}"),
        });

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert
        var usage = Assert.Single(
            result,
            item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.True(usage.IsAvailable);
        Assert.Equal("Google Gemini", usage.ProviderName);
        Assert.Equal(email, usage.AccountName);

        TestTempPaths.CleanupPath(tempDir);
    }

    [Fact]
    public async Task GetUsageAsync_DeduplicatesBuckets_AndDoesNotEmitLegacySlotBars_WithoutModelIdsAsync()
    {
        // Arrange
        var tempDir = TestTempPaths.CreateDirectory("gemini-bucket-dedupe-test");
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");

        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" },
            },
        }));

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, Path.Combine(tempDir, "oauth_creds_override.json"));

        this.SetupHttpResponse("https://oauth2.googleapis.com/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"access_token\":\"at\"}"),
        });

        var quotaResponse = new
        {
            buckets = new[]
            {
                new { remainingFraction = 0.679, resetTime = "2026-03-12T14:38:28Z", quotaId = "FreeTierRequestsPerMinute" },
                new { remainingFraction = 0.679, resetTime = "2026-03-12T14:38:28Z", quotaId = "FreeTierRequestsPerMinute" }, // duplicate
                new { remainingFraction = 0.889, resetTime = "2026-03-12T15:10:00Z", quotaId = "FreeTierRequestsPerHour" },
                new { remainingFraction = 0.975, resetTime = "2026-03-12T14:35:02Z", quotaId = "FreeTierRequestsPerDay" },
            },
        };

        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaResponse)),
        });

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert — buckets without modelId produce no flat cards; provider returns unavailable when no cards
        var resultList = result.ToList();
        Assert.All(resultList, u => Assert.Equal("gemini-cli", u.ProviderId));
        Assert.DoesNotContain(
            resultList,
            item => item.ProviderId.StartsWith("gemini-cli.", StringComparison.OrdinalIgnoreCase));

        TestTempPaths.CleanupPath(tempDir);
    }

    [Fact]
    public async Task GetUsageAsync_IncludesModelQuotaDetails_AndDoesNotEmitLegacySlotBars_WhenBucketsContainModelIdsAsync()
    {
        // Arrange
        var tempDir = TestTempPaths.CreateDirectory("gemini-model-quota-test");
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");

        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" },
            },
        }));

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, Path.Combine(tempDir, "oauth_creds_override.json"));

        this.SetupHttpResponse("https://oauth2.googleapis.com/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"access_token\":\"at\"}"),
        });

        var quotaResponse = new
        {
            buckets = new object[]
            {
                new { remainingFraction = 0.971, resetTime = "2030-03-12T14:53:00Z", quotaId = "FreeTierRequestsPerMinute", modelId = "gemini-2.5-flash-lite" },
                new { remainingFraction = 0.657, resetTime = "2030-03-12T14:56:00Z", quotaId = "FreeTierRequestsPerHour", modelId = "gemini-3-flash-preview" },
                new { remainingFraction = 0.657, resetTime = "2030-03-12T14:56:00Z", quotaId = "FreeTierRequestsPerHour", modelId = "gemini-2.5-flash" },
                new { remainingFraction = 0.000, resetTime = "2030-03-12T17:40:00Z", quotaId = "FreeTierRequestsPerDay", modelId = "gemini-2.5-pro" },
                new { remainingFraction = 0.000, resetTime = "2030-03-12T17:40:00Z", quotaId = "FreeTierRequestsPerDay", modelId = "gemini-3.1-pro-preview" },
            },
        };

        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaResponse)),
        });

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert — provider now emits one flat card per model (no parent card)
        var resultList = result.ToList();
        Assert.Equal(5, resultList.Count);
        Assert.All(resultList, u => Assert.Equal("gemini-cli", u.ProviderId));
        Assert.All(resultList, u => Assert.True(u.IsAvailable));
        Assert.All(resultList, u => Assert.False(string.IsNullOrWhiteSpace(u.Description)));

        Assert.Contains(resultList, u => string.Equals(u.ModelName, "gemini-2.5-pro", StringComparison.Ordinal));
        Assert.Contains(resultList, u => string.Equals(u.Name, "Gemini 3.1 Pro Preview", StringComparison.Ordinal));

        Assert.DoesNotContain(
            resultList,
            item => item.ProviderId.StartsWith("gemini-cli.", StringComparison.OrdinalIgnoreCase));

        TestTempPaths.CleanupPath(tempDir);
    }

    [Fact]
    public async Task GetUsageAsync_KeepsModelDetails_WhenModelIdsArePresentAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("gemini-partial-window-test");
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");

        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" },
            },
        }));

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, Path.Combine(tempDir, "oauth_creds_override.json"));

        this.SetupHttpResponse("https://oauth2.googleapis.com/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"access_token\":\"at\"}"),
        });

        var quotaResponse = new
        {
            buckets = new object[]
            {
                new { remainingFraction = 0.971, resetTime = "2030-03-12T14:53:00Z", modelId = "gemini-2.5-flash-lite" },
                new { remainingFraction = 0.657, resetTime = "2030-03-12T14:56:00Z", quotaId = "FreeTierRequestsPerHour", modelId = "gemini-3-flash-preview" },
                new { remainingFraction = 0.000, resetTime = "2030-03-12T17:40:00Z", quotaId = "FreeTierRequestsPerDay", modelId = "gemini-2.5-pro" },
            },
        };

        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaResponse)),
        });

        var result = await provider.GetUsageAsync(this.Config);

        // Provider now emits one flat card per model (no parent card)
        var resultList = result.ToList();
        Assert.Equal(3, resultList.Count);
        Assert.All(resultList, u => Assert.Equal("gemini-cli", u.ProviderId));
        Assert.Contains(resultList, u => string.Equals(u.ModelName, "gemini-2.5-flash-lite", StringComparison.Ordinal));
        Assert.Contains(resultList, u => string.Equals(u.ModelName, "gemini-3-flash-preview", StringComparison.Ordinal));
        Assert.Contains(resultList, u => string.Equals(u.ModelName, "gemini-2.5-pro", StringComparison.Ordinal));
        Assert.DoesNotContain(
            resultList,
            item => item.ProviderId.StartsWith("gemini-cli.", StringComparison.OrdinalIgnoreCase));

        TestTempPaths.CleanupPath(tempDir);
    }

    [Fact]
    public async Task GetUsageAsync_UsesModelLabels_WhenQuotaIdsDifferFromModelBucketsAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("gemini-quotaid-fallback-test");
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");

        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" },
            },
        }));

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, Path.Combine(tempDir, "oauth_creds_override.json"));

        this.SetupHttpResponse("https://oauth2.googleapis.com/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"access_token\":\"at\"}"),
        });

        var quotaResponse = new
        {
            buckets = new object[]
            {
                new { remainingFraction = 0.971, resetTime = "2030-03-12T14:53:00Z", quotaId = "FreeTierRequestsPerMinute" },
                new { remainingFraction = 0.657, resetTime = "2030-03-12T14:56:00Z", quotaId = "FreeTierRequestsPerHour" },
                new { remainingFraction = 0.000, resetTime = "2030-03-12T17:40:00Z", quotaId = "FreeTierRequestsPerDay" },
                new { remainingFraction = 0.971, resetTime = "2030-03-12T14:53:00Z", quotaId = "ModelMinute", modelId = "gemini-2.5-flash-lite" },
                new { remainingFraction = 0.657, resetTime = "2030-03-12T14:56:00Z", quotaId = "ModelHour", modelId = "gemini-3-flash-preview" },
                new { remainingFraction = 0.000, resetTime = "2030-03-12T17:40:00Z", quotaId = "ModelDay", modelId = "gemini-2.5-pro" },
            },
        };

        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaResponse)),
        });

        var result = await provider.GetUsageAsync(this.Config);

        // Provider now emits one flat card per model (buckets without modelId are ignored)
        var resultList = result.ToList();
        Assert.All(resultList, u => Assert.Equal("gemini-cli", u.ProviderId));
        Assert.Contains(resultList, u => string.Equals(u.Name, "Gemini 2.5 Flash Lite", StringComparison.Ordinal));
        Assert.Contains(resultList, u => string.Equals(u.Name, "Gemini 3 Flash Preview", StringComparison.Ordinal));
        Assert.Contains(resultList, u => string.Equals(u.Name, "Gemini 2.5 Pro", StringComparison.Ordinal));
        Assert.DoesNotContain(
            resultList,
            item => item.ProviderId.StartsWith("gemini-cli.", StringComparison.OrdinalIgnoreCase));

        TestTempPaths.CleanupPath(tempDir);
    }

    [Fact]
    public async Task GetUsageAsync_UsesRealGeminiFixtureShape_ForModelDetailsWithoutLegacySlotBarsAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("gemini-real-fixture-test");
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");

        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" },
            },
        }));

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, Path.Combine(tempDir, "oauth_creds_override.json"));

        this.SetupHttpResponse("https://oauth2.googleapis.com/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"access_token\":\"at\"}"),
        });

        var quotaFixture = LoadFixture("gemini_cli_retrieve_user_quota.snapshot.json");
        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(quotaFixture),
        });

        var result = await provider.GetUsageAsync(this.Config);

        // Provider now emits one flat card per model (no parent card)
        var resultList = result.ToList();
        Assert.All(resultList, u => Assert.Equal("gemini-cli", u.ProviderId));
        Assert.Contains(resultList, u => string.Equals(u.Name, "Gemini 2.5 Flash Lite", StringComparison.Ordinal));
        Assert.Contains(resultList, u => string.Equals(u.Name, "Gemini 3.1 Pro Preview", StringComparison.Ordinal));

        Assert.DoesNotContain(
            resultList,
            item => item.ProviderId.StartsWith("gemini-cli.", StringComparison.OrdinalIgnoreCase));

        TestTempPaths.CleanupPath(tempDir);
    }

    private string CreateUnsignedJwt(IDictionary<string, object> payload)
    {
        var headerJson = "{\"alg\":\"none\",\"typ\":\"JWT\"}";
        var payloadJson = JsonSerializer.Serialize(payload);
        return $"{Base64UrlEncode(headerJson)}.{Base64UrlEncode(payloadJson)}.";
    }

    private static string Base64UrlEncode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public async Task GetUsageAsync_RefreshToken_UsesClientSecretAndFallbackClientAsync()
    {
        // Arrange
        var cliClientId = GetPrivateConstValue(nameof(GeminiProvider), "GeminiCliClientId");
        var cliClientSecret = GetPrivateConstValue(nameof(GeminiProvider), "GeminiCliClientSecret");
        var pluginClientId = GetPrivateConstValue(nameof(GeminiProvider), "GeminiPluginClientId");
        var pluginClientSecret = GetPrivateConstValue(nameof(GeminiProvider), "GeminiPluginClientSecret");

        var tempDir = Path.Combine(Path.GetTempPath(), $"gemini-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");

        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" },
            },
        }));

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, Path.Combine(tempDir, "oauth_creds_override.json"));

        this.SetupHttpResponse(
            request =>
                request.RequestUri != null &&
                string.Equals(request.RequestUri.ToString(), "https://oauth2.googleapis.com/token", StringComparison.Ordinal) &&
                RequestContentContainsAsync(request, $"client_id={cliClientId}").GetAwaiter().GetResult() &&
                RequestContentContainsAsync(request, $"client_secret={cliClientSecret}").GetAwaiter().GetResult(),
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("{\"error\":\"unauthorized_client\"}"),
            });

        this.SetupHttpResponse(
            request =>
                request.RequestUri != null &&
                string.Equals(request.RequestUri.ToString(), "https://oauth2.googleapis.com/token", StringComparison.Ordinal) &&
                RequestContentContainsAsync(request, $"client_id={pluginClientId}").GetAwaiter().GetResult() &&
                RequestContentContainsAsync(request, $"client_secret={pluginClientSecret}").GetAwaiter().GetResult(),
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"access_token\":\"at\"}"),
            });

        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"buckets\":[{\"modelId\":\"gemini-2.5-flash\",\"remainingFraction\":0.7}]}"),
        });

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert
        var usage = Assert.Single(
            result,
            item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.True(usage.IsAvailable);

        // Content correctness is validated by the setup matchers above (which run during SendAsync,
        // before the request is disposed). Here we only verify call counts.
        this.MessageHandler.Protected()
            .Verify(
                "SendAsync",
                Times.Exactly(2),
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.RequestUri != null &&
                    request.RequestUri.ToString() == "https://oauth2.googleapis.com/token"),
                ItExpr.IsAny<CancellationToken>());

        Directory.Delete(tempDir, recursive: true);
    }

    private static async Task<bool> RequestContentContainsAsync(HttpRequestMessage request, string value)
    {
        if (request.Content == null)
        {
            return false;
        }

        var content = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return content?.Contains(value, StringComparison.Ordinal) == true;
    }

    private static string GetPrivateConstValue(string typeName, string fieldName)
    {
        var providerType = typeof(GeminiProvider);
        var field = providerType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetRawConstantValue() as string;
        Assert.False(string.IsNullOrWhiteSpace(value), $"Expected non-empty const field '{fieldName}' on type '{typeName}'.");
        return value!;
    }

    // --- Phase 4: FailureContext attachment ---
    [Fact]
    public async Task GetUsageAsync_AllAccountsFail_AttachesFailureContextOnUnavailableRowAsync()
    {
        // Arrange: account exists but token refresh always fails (network error)
        var tempDir = TestTempPaths.CreateDirectory("gemini-failure-context-test");
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");

        await File.WriteAllTextAsync(accountsPath, JsonSerializer.Serialize(new
        {
            accounts = new[]
            {
                new { email = "user@example.com", refreshToken = "rt", projectId = "proj1" },
            },
        }));

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, Path.Combine(tempDir, "oauth_creds_override.json"));

        this.MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var result = await provider.GetUsageAsync(this.Config);
        var usage = result.First();

        // Output behavior is unchanged
        Assert.False(usage.IsAvailable);
        Assert.Equal(ProviderUsageState.Error, usage.State);
        Assert.Contains("Failed to fetch quota", usage.Description, StringComparison.OrdinalIgnoreCase);

        // FailureContext is now attached with the last captured exception classification
        Assert.NotNull(usage.FailureContext);
        Assert.Equal(HttpFailureClassification.Network, usage.FailureContext!.Classification);
        Assert.True(usage.FailureContext.IsLikelyTransient);

        TestTempPaths.CleanupPath(tempDir);
    }

    [Fact]
    public async Task GetUsageAsync_AllAccountsFail_NoFailureContextWhenNoAccountsTried()
    {
        // Arrange: no accounts at all — FailureContext should be null (no HTTP failure occurred)
        var tempDir = TestTempPaths.CreateDirectory("gemini-no-accounts-context-test");
        var missingAccountsPath = Path.Combine(tempDir, "missing.json");
        var missingOauthPath = Path.Combine(tempDir, "missing_oauth.json");

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, missingAccountsPath, missingOauthPath);

        // Act
        var result = await provider.GetUsageAsync(this.Config);
        var usage = result.First();

        // Output behavior: missing state, no failure context (no HTTP call was made)
        Assert.False(usage.IsAvailable);
        Assert.Equal(ProviderUsageState.Missing, usage.State);
        Assert.Null(usage.FailureContext);

        TestTempPaths.CleanupPath(tempDir);
    }
}
