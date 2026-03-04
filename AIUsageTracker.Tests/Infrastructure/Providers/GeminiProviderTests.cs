using System.Net;
using System.Text;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class GeminiProviderTests
{
    private const string DefaultClientId = "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
    private const string PluginClientId = "681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";

    [Fact]
    public async Task GetUsageAsync_RetriesWithPluginClient_WhenDefaultClientUnauthorized()
    {
        var tempDir = CreateTempDir();
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");
        File.WriteAllText(accountsPath, """
            {
              "accounts": [
                {
                  "email": "user@example.com",
                  "refreshToken": "refresh-token-1",
                  "projectId": "proj-1"
                }
              ]
            }
            """);

        var tokenCallPayloads = new List<string>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    var form = await request.Content!.ReadAsStringAsync();
                    tokenCallPayloads.Add(form);

                    if (form.Contains($"client_id={Uri.EscapeDataString(DefaultClientId)}", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("""{"error":"unauthorized_client"}""", Encoding.UTF8, "application/json")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"access_token":"access-token-1"}""", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri?.AbsoluteUri == "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"buckets":[{"modelId":"gemini-3-pro","remainingFraction":0.8,"resetTime":"2030-01-01T00:00:00Z"}]}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                throw new InvalidOperationException($"Unexpected URL: {request.RequestUri}");
            });

        var provider = new GeminiProvider(
            new HttpClient(handler.Object),
            new Mock<ILogger<GeminiProvider>>().Object,
            accountsPath,
            oauthCredsPathOverride: null);

        var config = new ProviderConfig { ProviderId = "gemini-cli", ApiKey = "n/a" };
        var usage = (await provider.GetUsageAsync(config)).Single();

        Assert.True(usage.IsAvailable);
        Assert.Equal(80.0, usage.RequestsPercentage, 1);
        Assert.Equal(200, usage.HttpStatus);
        Assert.Contains("\"buckets\"", usage.RawJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(2, tokenCallPayloads.Count);
        Assert.Contains(tokenCallPayloads, p => p.Contains($"client_id={Uri.EscapeDataString(DefaultClientId)}", StringComparison.Ordinal));
        Assert.Contains(tokenCallPayloads, p => p.Contains($"client_id={Uri.EscapeDataString(PluginClientId)}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetUsageAsync_PrefersPluginClient_WhenOauthCredsAudienceMatchesPlugin()
    {
        var tempDir = CreateTempDir();
        var accountsPath = Path.Combine(tempDir, "antigravity-accounts.json");
        var oauthPath = Path.Combine(tempDir, "oauth_creds.json");

        File.WriteAllText(accountsPath, """
            {
              "accounts": [
                {
                  "email": "user@example.com",
                  "refreshToken": "refresh-token-1",
                  "projectId": "proj-1"
                }
              ]
            }
            """);
        File.WriteAllText(oauthPath, $$"""
            {
              "id_token": "{{CreateJwtWithAudience(PluginClientId)}}"
            }
            """);

        var tokenCallPayloads = new List<string>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    var form = await request.Content!.ReadAsStringAsync();
                    tokenCallPayloads.Add(form);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"access_token":"access-token-1"}""", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri?.AbsoluteUri == "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"buckets":[{"remainingFraction":1.0}]}""", Encoding.UTF8, "application/json")
                    };
                }

                throw new InvalidOperationException($"Unexpected URL: {request.RequestUri}");
            });

        var provider = new GeminiProvider(
            new HttpClient(handler.Object),
            new Mock<ILogger<GeminiProvider>>().Object,
            accountsPath,
            oauthPath);

        var config = new ProviderConfig { ProviderId = "gemini-cli", ApiKey = "n/a" };
        var usage = (await provider.GetUsageAsync(config)).Single();

        Assert.True(usage.IsAvailable);
        Assert.Single(tokenCallPayloads);
        Assert.Contains($"client_id={Uri.EscapeDataString(PluginClientId)}", tokenCallPayloads[0], StringComparison.Ordinal);
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "temp", "gemini-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateJwtWithAudience(string audience)
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncode($$"""{"aud":"{{audience}}"}""");
        return $"{header}.{payload}.";
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
