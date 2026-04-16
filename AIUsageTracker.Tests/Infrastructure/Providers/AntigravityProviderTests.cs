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
    public async Task GetUsageAsync_WhenNotRunning_ReturnsQuotaPlanTypeAsync()
    {
        // Arrange
        this.Config.ApiKey = string.Empty;

        // Act - Antigravity is not running in test env
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.First();
        Assert.Equal("antigravity", usage.ProviderId);
        Assert.Equal("Google Antigravity", usage.ProviderName);
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

    [Fact]
    public async Task FetchUsage_MixedQuotaData_PreservesUnknownModelEntriesInDetailsAsync()
    {
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
              "quotaInfo": {
                "remainingFraction": 0.42
              }
            },
            {
              "label": "gpt-oss",
              "modelOrAlias": {
                "model": "gpt-oss"
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

        var usages = await InvokeFetchUsageAsync(this._provider, 5109, "csrf-token", this.Config);
        var summary = usages.First();

        // In the flat-card model, each model becomes its own child ProviderUsage card.
        Assert.Contains(usages, u => u.ProviderId.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FetchUsage_FullRemainingQuota_UsesSemanticRemainingText_AndPreservesChildPercentagesAsync()
    {
        var snapshotJson = LoadFixture("antigravity_user_status.snapshot.json");

        this.SetupHttpResponse(_ => true, new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(snapshotJson),
        });

        var usages = await InvokeFetchUsageAsync(this._provider, 5109, "csrf-token", this.Config);
        var summary = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "antigravity", StringComparison.Ordinal));
        var geminiFlashChild = Assert.Single(
            usages,
            usage => string.Equals(usage.ProviderId, "antigravity.gemini-3-flash", StringComparison.Ordinal));

        Assert.Equal("Google Antigravity", summary.ProviderName);

        Assert.Equal(0, geminiFlashChild.UsedPercent); // 0% used (100% remaining)
        Assert.Equal(0, geminiFlashChild.RequestsUsed);
        Assert.Equal(100, geminiFlashChild.RequestsAvailable);
        Assert.True(geminiFlashChild.DisplayAsFraction);
        Assert.Equal("100% Remaining", geminiFlashChild.Description);
    }

    private static async Task<List<ProviderUsage>> InvokeFetchUsageAsync(AntigravityProvider provider, int port, string csrfToken, ProviderConfig config)
    {
        var method = typeof(AntigravityProvider).GetMethod("FetchUsageAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<List<ProviderUsage>>)method!.Invoke(provider, new object[] { port, csrfToken, config })!;
        return await task.ConfigureAwait(false);
    }
}
