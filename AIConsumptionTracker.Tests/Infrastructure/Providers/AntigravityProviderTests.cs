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
        Assert.Equal("claude-3-7-sonnet", claude.ModelName);
        Assert.Equal("80%", claude.Used);

        Assert.Equal("OpenAI", gpt.GroupName);
        Assert.Equal("gpt-4.1", gpt.ModelName);
        Assert.Equal("25%", gpt.Used);

        Assert.Equal("Experimental", mystery.GroupName);
        Assert.Equal("mystery-model", mystery.ModelName);
        Assert.Equal("50%", mystery.Used);

        Assert.DoesNotContain(details, d => d.GroupName == "Ungrouped Models");
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
