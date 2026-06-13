// <copyright file="XaiProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class XaiProviderTests : HttpProviderTestBase<XaiProvider>
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly XaiProvider _provider;

    public XaiProviderTests()
    {
        this._provider = new XaiProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ShowsActiveStatusAsync()
    {
        var responseJson = """
        {
          "redacted_api_key": "xai-...b14o",
          "user_id": "59fbe5f2-040b-46d5-8325-868bb8f23eb2",
          "name": "My API Key",
          "team_id": "5ea6f6bd-7815-4b8a-9135-28b2d7ba6722",
          "acls": ["api-key:model:*", "api-key:endpoint:*"],
          "api_key_id": "ae1e1841-4326-4b36-a8a9-8a1a7237db11",
          "team_blocked": false,
          "api_key_blocked": false,
          "api_key_disabled": false
        }
        """;

        this.SetupHttpResponse("https://api.x.ai/v1/api-key", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = Assert.Single(result);

        Assert.True(usage.IsAvailable);
        Assert.Contains("Active", usage.Description, StringComparison.Ordinal);
        Assert.Contains("My API Key", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_NoKeyName_ShowsActiveOnlyAsync()
    {
        var responseJson = """
        {
          "name": "",
          "team_blocked": false,
          "api_key_blocked": false,
          "api_key_disabled": false
        }
        """;

        this.SetupHttpResponse("https://api.x.ai/v1/api-key", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = Assert.Single(result);

        Assert.True(usage.IsAvailable);
        Assert.Equal("Active", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_KeyBlocked_ReturnsUnavailableAsync()
    {
        var responseJson = """
        {
          "name": "Test Key",
          "team_blocked": false,
          "api_key_blocked": true,
          "api_key_disabled": false
        }
        """;

        this.SetupHttpResponse("https://api.x.ai/v1/api-key", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = Assert.Single(result);

        Assert.False(usage.IsAvailable);
        Assert.Contains("blocked", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_KeyDisabled_ReturnsUnavailableAsync()
    {
        var responseJson = """
        {
          "name": "Test Key",
          "team_blocked": false,
          "api_key_blocked": false,
          "api_key_disabled": true
        }
        """;

        this.SetupHttpResponse("https://api.x.ai/v1/api-key", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = Assert.Single(result);

        Assert.False(usage.IsAvailable);
        Assert.Contains("disabled", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_TeamBlocked_ReturnsUnavailableAsync()
    {
        var responseJson = """
        {
          "name": "Test Key",
          "team_blocked": true,
          "api_key_blocked": false,
          "api_key_disabled": false
        }
        """;

        this.SetupHttpResponse("https://api.x.ai/v1/api-key", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = Assert.Single(result);

        Assert.False(usage.IsAvailable);
        Assert.Contains("blocked", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_MissingApiKey_ReturnsMissingStateAsync()
    {
        this.Config.ApiKey = string.Empty;

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = Assert.Single(result);

        Assert.False(usage.IsAvailable);
        Assert.Equal(ProviderUsageState.Missing, usage.State);
        Assert.Contains("API Key missing", usage.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HttpFailureClassification.Authentication, false)]
    [InlineData(HttpStatusCode.Forbidden, HttpFailureClassification.Authorization, false)]
    [InlineData(HttpStatusCode.TooManyRequests, HttpFailureClassification.RateLimit, true)]
    [InlineData(HttpStatusCode.InternalServerError, HttpFailureClassification.Server, true)]
    public async Task GetUsageAsync_HttpError_AttachesFailureContextAsync(
        HttpStatusCode statusCode,
        HttpFailureClassification expectedClassification,
        bool expectedTransient)
    {
        this.SetupHttpResponse("https://api.x.ai/v1/api-key", new HttpResponseMessage
        {
            StatusCode = statusCode,
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.First();

        Assert.True(usage.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(usage.Description));
        Assert.Equal((int)statusCode, usage.HttpStatus);

        Assert.NotNull(usage.FailureContext);
        Assert.Equal(expectedClassification, usage.FailureContext!.Classification);
        Assert.Equal(expectedTransient, usage.FailureContext.IsLikelyTransient);
    }

    [Fact]
    public async Task GetUsageAsync_NetworkException_AttachesNetworkFailureContextAsync()
    {
        this.MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.First();

        Assert.False(usage.IsAvailable);
        Assert.Contains("Connection failed", usage.Description, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(usage.FailureContext);
        Assert.Equal(HttpFailureClassification.Network, usage.FailureContext!.Classification);
        Assert.True(usage.FailureContext.IsLikelyTransient);
    }

    [Fact]
    public void StaticDefinition_HasCorrectProviderId()
    {
        Assert.Equal("xai", XaiProvider.StaticDefinition.ProviderId);
    }

    [Fact]
    public void StaticDefinition_HasCorrectDisplayName()
    {
        Assert.Equal("xAI (Grok)", XaiProvider.StaticDefinition.DisplayName);
    }

    [Fact]
    public void StaticDefinition_DiscoversViaXaiApiKeyEnvVar()
    {
        Assert.Contains("XAI_API_KEY", XaiProvider.StaticDefinition.DiscoveryEnvironmentVariables);
    }

    [Fact]
    public void StaticDefinition_IsUsageTypeNotQuotaBased()
    {
        Assert.Equal(PlanType.Usage, XaiProvider.StaticDefinition.PlanType);
        Assert.False(XaiProvider.StaticDefinition.IsQuotaBased);
    }
}
