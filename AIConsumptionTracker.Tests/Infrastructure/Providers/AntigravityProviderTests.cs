using System.Net;
using System.Reflection;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class AntigravityProviderTests
{
    private readonly Mock<ILogger<AntigravityProvider>> _logger;
    private readonly AntigravityProvider _provider;
    private readonly Mock<HttpMessageHandler> _messageHandler;
    private readonly HttpClient _httpClient;

    public AntigravityProviderTests()
    {
        _logger = new Mock<ILogger<AntigravityProvider>>();
        _provider = new AntigravityProvider(_logger.Object);
        _messageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_messageHandler.Object);
    }

    [Fact]
    public async Task GetUsageAsync_WhenNotRunning_ReturnsQuotaPlanType()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "antigravity", ApiKey = "" };

        // Act - Antigravity is not running, so it will return "Not running"
        var result = await _provider.GetUsageAsync(config);

        // Assert
        // Use First() instead of Single() because on a dev machine with the app running, 
        // it might return actual process data.
        var usage = result.First();
        Console.WriteLine($"DEBUG: ProviderId={usage.ProviderId}, IsQuotaBased={usage.IsQuotaBased}, PlanType={usage.PlanType}, Description={usage.Description}");
        Assert.Equal("antigravity", usage.ProviderId);
        Assert.True(usage.IsQuotaBased, "Antigravity should be quota-based even when not running");
        Assert.Equal(PlanType.Coding, usage.PlanType);
    }

    [Fact]
    public async Task GetUsageAsync_Result_ShouldAlwaysHaveQuotaPlanType()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "antigravity", ApiKey = "" };

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert - All returned usages should have Quota payment type
        Assert.All(result, usage =>
        {
            Assert.True(usage.IsQuotaBased, $"Provider {usage.ProviderId} should have IsQuotaBased=true");
            Assert.Equal(PlanType.Coding, usage.PlanType);
        });
    }

    [Fact]
    public async Task GetUsageAsync_WhenCachedResetPassedAndOffline_ReturnsUnknownStatus()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "antigravity", ApiKey = "" };
        var cachedUsage = new ProviderUsage
        {
            ProviderId = "antigravity",
            ProviderName = "Antigravity",
            IsAvailable = true,
            RequestsPercentage = 35,
            RequestsUsed = 65,
            RequestsAvailable = 100,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = "35% Remaining",
            Details = new List<ProviderUsageDetail>
            {
                new()
                {
                    Name = "gpt-4.1",
                    Used = "35%",
                    NextResetTime = DateTime.Now.AddMinutes(-5)
                }
            }
        };

        typeof(AntigravityProvider).GetField("_cachedUsage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_provider, cachedUsage);
        typeof(AntigravityProvider).GetField("_cacheTimestamp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_provider, DateTime.Now.AddMinutes(-10));
        typeof(AntigravityProvider).GetField("_cachedProcessInfos", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_provider, new List<(int Pid, string Token, int? Port)>());
        typeof(AntigravityProvider).GetField("_lastProcessCheck", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_provider, DateTime.Now);

        // Act
        var result = await _provider.GetUsageAsync(config);
        var usage = Assert.Single(result);

        // Assert
        Assert.Contains("Status unknown", usage.Description);
        Assert.Null(usage.Details);
        Assert.Equal(0, usage.RequestsPercentage);
        Assert.Equal(0, usage.RequestsUsed);
    }

    [Fact]
    public async Task FetchUsage_SnapshotPayload_ParsesGroupsAndModelsConsistently()
    {
        // Arrange
        var snapshotJson = LoadFixture("antigravity_user_status.snapshot.json");
        var provider = CreateProviderWithHttpClient();
        var config = new ProviderConfig { ProviderId = "antigravity" };

        _messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Post &&
                    request.RequestUri!.ToString().Contains("/GetUserStatus", StringComparison.Ordinal) &&
                    request.Headers.Contains("X-Codeium-Csrf-Token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(snapshotJson)
            });

        // Act
        var usages = await InvokeFetchUsageAsync(provider, 5109, "csrf-token", config);
        var summary = usages.First();

        // Assert
        Assert.Equal("antigravity", summary.ProviderId);
        Assert.True(summary.IsQuotaBased);
        Assert.Equal(PlanType.Coding, summary.PlanType);
        Assert.Equal(25.0, summary.RequestsPercentage);
        Assert.NotNull(summary.Details);

        var details = summary.Details!;
        var claude = Assert.Single(details, d => d.Name == "claude-3.7-sonnet");
        var gpt = Assert.Single(details, d => d.Name == "gpt-4.1");
        var mystery = Assert.Single(details, d => d.Name == "mystery-model");

        Assert.Equal("Anthropic", claude.GroupName);
        Assert.Equal("claude-3.7-sonnet", claude.ModelName);
        Assert.Equal("80%", claude.Used);

        Assert.Equal("OpenAI", gpt.GroupName);
        Assert.Equal("gpt-4.1", gpt.ModelName);
        Assert.Equal("25%", gpt.Used);

        Assert.Equal("Experimental", mystery.GroupName);
        Assert.Equal("mystery-model", mystery.ModelName);
        Assert.Equal("50%", mystery.Used);

        Assert.DoesNotContain(details, d => d.GroupName == "Ungrouped Models");
    }

    [Fact]
    public async Task FetchUsage_MissingQuotaInfo_ReturnsUnknownUsage()
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
                },
                {
                  "label": "gemini-3.1-pro",
                  "modelOrAlias": {
                    "model": "gemini-3.1-pro"
                  }
                }
              ],
              "clientModelSorts": [
                {
                  "name": "Gemini",
                  "groups": [
                    {
                      "name": "Google",
                      "modelLabels": [
                        "gemini-3-pro",
                        "gemini-3.1-pro"
                      ]
                    }
                  ]
                }
              ]
            }
          }
        }
        """;
        var provider = CreateProviderWithHttpClient();
        var config = new ProviderConfig { ProviderId = "antigravity" };

        _messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Post &&
                    request.RequestUri!.ToString().Contains("/GetUserStatus", StringComparison.Ordinal) &&
                    request.Headers.Contains("X-Codeium-Csrf-Token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(snapshotJson)
            });

        // Act
        var usages = await InvokeFetchUsageAsync(provider, 5109, "csrf-token", config);
        var summary = usages.First();

        // Assert
        Assert.Contains("Usage unknown", summary.Description);
        Assert.True(summary.Details == null || !summary.Details.Any());
        Assert.Single(usages);
    }

    [Fact]
    public async Task FetchUsage_UsesLabelAsModelName_WhenModelAliasIsPlaceholder()
    {
        // Arrange
        var snapshotJson = """
        {
          "userStatus": {
            "email": "snapshot@example.com",
            "cascadeModelConfigData": {
              "clientModelConfigs": [
                {
                  "label": "Claude Sonnet 4.6 (Thinking)",
                  "modelOrAlias": {
                    "model": "MODEL_PLACEHOLDER_M35"
                  },
                  "quotaInfo": {
                    "remainingFraction": 0.6
                  }
                }
              ],
              "clientModelSorts": [
                {
                  "name": "Recommended",
                  "groups": [
                    {
                      "name": "Tier 1",
                      "modelLabels": [
                        "Claude Sonnet 4.6 (Thinking)"
                      ]
                    }
                  ]
                }
              ]
            }
          }
        }
        """;
        var provider = CreateProviderWithHttpClient();
        var config = new ProviderConfig { ProviderId = "antigravity" };

        _messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Post &&
                    request.RequestUri!.ToString().Contains("/GetUserStatus", StringComparison.Ordinal) &&
                    request.Headers.Contains("X-Codeium-Csrf-Token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(snapshotJson)
            });

        // Act
        var usages = await InvokeFetchUsageAsync(provider, 5109, "csrf-token", config);
        var summary = usages.First();
        var detail = Assert.Single(summary.Details!);

        // Assert
        Assert.Equal("Claude Sonnet 4.6 (Thinking)", detail.Name);
        Assert.Equal("Claude Sonnet 4.6 (Thinking)", detail.ModelName);
    }

    [Fact]
    public async Task GetUsageAsync_WhenCachedAndOffline_ReturnsUnknownWithoutModelDetails()
    {
        // Arrange
        var config = new ProviderConfig { ProviderId = "antigravity", ApiKey = "" };
        var cachedUsage = new ProviderUsage
        {
            ProviderId = "antigravity",
            ProviderName = "Antigravity",
            IsAvailable = true,
            RequestsPercentage = 12,
            RequestsUsed = 88,
            RequestsAvailable = 100,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = "12% Remaining",
            Details = new List<ProviderUsageDetail>
            {
                new()
                {
                    Name = "claude-opus",
                    Used = "12%",
                    NextResetTime = DateTime.Now.AddMinutes(45)
                }
            }
        };

        typeof(AntigravityProvider).GetField("_cachedUsage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_provider, cachedUsage);
        typeof(AntigravityProvider).GetField("_cacheTimestamp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_provider, DateTime.Now.AddMinutes(-8));
        typeof(AntigravityProvider).GetField("_cachedProcessInfos", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_provider, new List<(int Pid, string Token, int? Port)>());
        typeof(AntigravityProvider).GetField("_lastProcessCheck", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_provider, DateTime.Now);

        // Act
        var result = await _provider.GetUsageAsync(config);
        var usage = Assert.Single(result);

        // Assert
        Assert.Contains("Last refreshed", usage.Description);
        Assert.Contains("Usage unknown", usage.Description);
        Assert.Null(usage.Details);
        Assert.Equal(0, usage.RequestsPercentage);
        Assert.Equal(0, usage.RequestsUsed);
    }

    private AntigravityProvider CreateProviderWithHttpClient()
    {
        var ctor = typeof(AntigravityProvider).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(HttpClient), typeof(ILogger<AntigravityProvider>) },
            modifiers: null);

        Assert.NotNull(ctor);
        return (AntigravityProvider)ctor!.Invoke(new object[] { _httpClient, _logger.Object });
    }

    private static async Task<List<ProviderUsage>> InvokeFetchUsageAsync(AntigravityProvider provider, int port, string csrfToken, ProviderConfig config)
    {
        var method = typeof(AntigravityProvider).GetMethod("FetchUsage", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<List<ProviderUsage>>)method!.Invoke(provider, new object[] { port, csrfToken, config })!;
        return await task;
    }

    private static string LoadFixture(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "Providers", fileName);
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");
        return File.ReadAllText(fixturePath);
    }
}
