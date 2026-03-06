using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public sealed class JsonConfigLoaderPersistenceTests : IntegrationTestBase
{
    [Fact]
    public async Task SaveConfigAsync_DoesNotPersistNonPersistedProviderIds()
    {
        var authPath = CreateFile("config/auth.json", "{\"codex.spark\":{\"key\":\"legacy\"}}");
        var providersPath = CreateFile("config/providers.json", "{\"codex.spark\":{\"type\":\"quota-based\"}}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(TestRootPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(Path.Combine(TestRootPath, "preferences.json"));
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(TestRootPath);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(TestRootPath, "logs"));

        var loader = new JsonConfigLoader(
            logger: NullLogger<JsonConfigLoader>.Instance,
            tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var configs = new List<ProviderConfig>
        {
            new()
            {
                ProviderId = "codex",
                ApiKey = "codex-key",
                Type = "quota-based",
                PlanType = PlanType.Coding
            },
            new()
            {
                ProviderId = "codex.spark",
                ApiKey = "spark-key",
                Type = "quota-based",
                PlanType = PlanType.Coding
            }
        };

        await loader.SaveConfigAsync(configs);

        var auth = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await File.ReadAllTextAsync(authPath));
        var providers = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await File.ReadAllTextAsync(providersPath));

        Assert.NotNull(auth);
        Assert.NotNull(providers);
        Assert.Contains("codex", auth!.Keys);
        Assert.Contains("codex", providers!.Keys);
        Assert.DoesNotContain("codex.spark", auth.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("codex.spark", providers.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
