// <copyright file="GeminiProviderTests.cs" company="AIUsageTracker">
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
                new { remainingFraction = 0.8, resetTime = "2026-03-10T12:00:00Z" },
            },
        };

        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaResponse)),
        });

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert
        var usage = Assert.Single(
            result,
            item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.True(usage.IsAvailable);
        Assert.Equal("user@example.com", usage.AccountName);
        Assert.Contains("80", usage.RequestsPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("20", usage.RequestsUsed.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("80", usage.Description, StringComparison.Ordinal);

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
        var idToken = this.CreateUnsignedJwt(new Dictionary<string, object>
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
            Content = new StringContent("{\"buckets\":[{\"remainingFraction\":0.65,\"resetTime\":\"2026-03-10T12:00:00Z\"}]}"),
        });

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert
        var usage = Assert.Single(
            result,
            item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.True(usage.IsAvailable);
        Assert.Equal(email, usage.AccountName);
        Assert.Equal("Gemini CLI", usage.ProviderName);

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

        // Assert
        var usage = Assert.Single(
            result,
            item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.True(usage.IsAvailable);
        Assert.Null(usage.Details);

        var slotBars = result
            .Where(item =>
                string.Equals(item.ProviderId, "gemini-cli.minute", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.ProviderId, "gemini-cli.hourly", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.ProviderId, "gemini-cli.daily", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(slotBars);

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

        // Assert
        var usage = Assert.Single(
            result,
            item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.True(usage.IsAvailable);
        Assert.NotNull(usage.Details);

        var modelDetails = usage.Details!
            .Where(detail => detail.DetailType == ProviderUsageDetailType.Model)
            .OrderBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.Equal(5, modelDetails.Count);
        Assert.Contains(modelDetails, detail => detail.ModelName == "gemini-2.5-pro");
        Assert.Contains(modelDetails, detail => detail.Name == "Gemini 3.1 Pro Preview");
        Assert.All(modelDetails, detail => Assert.False(string.IsNullOrWhiteSpace(detail.Used)));

        Assert.Single(result);
        Assert.DoesNotContain(
            result,
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

        var summary = Assert.Single(result, item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.NotNull(summary.Details);
        var modelDetails = summary.Details!
            .Where(detail => detail.DetailType == ProviderUsageDetailType.Model)
            .OrderBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.Equal(3, modelDetails.Count);
        Assert.Contains(modelDetails, detail => detail.ModelName == "gemini-2.5-flash-lite");
        Assert.Contains(modelDetails, detail => detail.ModelName == "gemini-3-flash-preview");
        Assert.Contains(modelDetails, detail => detail.ModelName == "gemini-2.5-pro");
        Assert.DoesNotContain(
            result,
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

        var summary = Assert.Single(result, item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.NotNull(summary.Details);
        var modelDetails = summary.Details!
            .Where(detail => detail.DetailType == ProviderUsageDetailType.Model)
            .ToList();
        Assert.Contains(modelDetails, detail => detail.Name == "Gemini 2.5 Flash Lite");
        Assert.Contains(modelDetails, detail => detail.Name == "Gemini 3 Flash Preview");
        Assert.Contains(modelDetails, detail => detail.Name == "Gemini 2.5 Pro");
        Assert.DoesNotContain(
            result,
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

        var summary = Assert.Single(result, item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.NotNull(summary.Details);
        Assert.Contains(summary.Details!, detail => detail.DetailType == ProviderUsageDetailType.Model && detail.Name == "Gemini 2.5 Flash Lite");
        Assert.Contains(summary.Details!, detail => detail.DetailType == ProviderUsageDetailType.Model && detail.Name == "Gemini 3.1 Pro Preview");

        Assert.DoesNotContain(
            result,
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
                request.RequestUri.ToString() == "https://oauth2.googleapis.com/token" &&
                RequestContentContains(request, $"client_id={cliClientId}") &&
                RequestContentContains(request, $"client_secret={cliClientSecret}"),
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("{\"error\":\"unauthorized_client\"}"),
            });

        this.SetupHttpResponse(
            request =>
                request.RequestUri != null &&
                request.RequestUri.ToString() == "https://oauth2.googleapis.com/token" &&
                RequestContentContains(request, $"client_id={pluginClientId}") &&
                RequestContentContains(request, $"client_secret={pluginClientSecret}"),
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"access_token\":\"at\"}"),
            });

        this.SetupHttpResponse("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"buckets\":[{\"remainingFraction\":0.7}]}"),
        });

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert
        var usage = Assert.Single(
            result,
            item => string.Equals(item.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.True(usage.IsAvailable);

        this.MessageHandler.Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.RequestUri != null &&
                    request.RequestUri.ToString() == "https://oauth2.googleapis.com/token" &&
                    RequestContentContains(request, $"client_id={cliClientId}") &&
                    RequestContentContains(request, $"client_secret={cliClientSecret}")),
                ItExpr.IsAny<CancellationToken>());

        this.MessageHandler.Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.RequestUri != null &&
                    request.RequestUri.ToString() == "https://oauth2.googleapis.com/token" &&
                    RequestContentContains(request, $"client_id={pluginClientId}") &&
                    RequestContentContains(request, $"client_secret={pluginClientSecret}")),
                ItExpr.IsAny<CancellationToken>());

        Directory.Delete(tempDir, recursive: true);
    }

    private static bool RequestContentContains(HttpRequestMessage request, string value)
    {
        var content = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
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
}
