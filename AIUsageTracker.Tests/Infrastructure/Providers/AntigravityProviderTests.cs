// <copyright file="AntigravityProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Reflection;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class AntigravityProviderTests : HttpProviderTestBase<AntigravityProvider>
{
    private readonly AntigravityProvider _provider;

    public AntigravityProviderTests()
    {
        this._provider = new AntigravityProvider(this.HttpClient, this.Logger.Object);
    }

    [Fact]
    public void CreateLocalhostClient_HasShorterTimeout()
    {
        // Act
        var method = typeof(AntigravityProvider).GetMethod("CreateLocalhostClient", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var client = (HttpClient)method.Invoke(null, null)!;

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1.5), client.Timeout);
    }

    [Fact]
    public async Task GetUsageAsync_WhenNotRunning_ReturnsQuotaPlanTypeAsync()
    {
        // Arrange
        this.Config.ApiKey = string.Empty;

        // Act - Antigravity is not running in test env
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.First();
        Assert.Equal("antigravity", usage.ProviderId);
        Assert.True(usage.IsQuotaBased);
        Assert.Equal(PlanType.Coding, usage.PlanType);
    }

    [Fact]
    public async Task FetchUsage_MissingQuotaInfo_ReturnsUnknownUsageAsync()
    {
        // Arrange
        var snapshotJson = """
    {
      "userStatus": {
        "email": "snapshot@example.com",
        "cascadeModelConfigData": {
          "clientModelConfigs": [
            {
              "label": "gemini-3-pro",
              "modelOrAlias": {
                "model": "gemini-3-pro"
              },
              "quotaInfo": {}
            }
          ]
        }
      }
    }
    """;

        this.SetupHttpResponse(_ => true, new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(snapshotJson),
        });

        // Act
        var usages = await InvokeFetchUsageAsync(this._provider, 5109, "csrf-token", this.Config);
        var summary = usages.First();

        // Assert
        Assert.Contains("Usage unknown", summary.Description, StringComparison.Ordinal);
    }

    private static async Task<List<ProviderUsage>> InvokeFetchUsageAsync(AntigravityProvider provider, int port, string csrfToken, ProviderConfig config)
    {
        var method = typeof(AntigravityProvider).GetMethod("FetchUsageAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<List<ProviderUsage>>)method!.Invoke(provider, new object[] { port, csrfToken, config })!;
        return await task.ConfigureAwait(false);
    }
}
