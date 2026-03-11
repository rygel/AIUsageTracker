// <copyright file="GeminiProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text;
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

        var provider = new GeminiProvider(this.HttpClient, this.Logger.Object, accountsPath, null);

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
        var usage = result.Single();
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
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal(email, usage.AccountName);
        Assert.Equal("Gemini CLI", usage.ProviderName);

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
}
