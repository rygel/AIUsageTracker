using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public class ConfigLoaderTests : IntegrationTestBase
{
    [Fact]
    public async Task LoadConfigAsync_CanonicalizesHandledProviderAliases()
    {
        var authPath = CreateFile("config/auth.json", "{\"kimi-for-coding\":{\"key\":\"kimi-test-key\",\"type\":\"quota-based\"}}");
        var providersPath = CreateFile("config/providers.json", "{}");

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

        var configs = await loader.LoadConfigAsync();

        var kimi = Assert.Single(configs, config => config.ProviderId == "kimi");
        Assert.Equal("kimi-test-key", kimi.ApiKey);
        Assert.DoesNotContain(configs, config => config.ProviderId == "kimi-for-coding");
    }

    [Fact]
    public async Task LoadConfigAsync_PreservesConfiguredApiKey_WhenDiscoveryFindsEnvironmentKey()
    {
        var authPath = CreateFile("config/auth.json", "{\"openai\":{\"key\":\"sk-configured-key\"}}");
        var providersPath = CreateFile("config/providers.json", "{}");

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

        var priorValue = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-env-key");

            var configs = await loader.LoadConfigAsync();

            var openAi = Assert.Single(configs, config => config.ProviderId == "openai");
            Assert.Equal("sk-configured-key", openAi.ApiKey);
            Assert.Equal("Config: auth.json", openAi.AuthSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", priorValue);
        }
    }

    [Fact]
    public async Task LoadConfigAsync_UsesDiscoveryKey_ForExistingProviderSettingsWithoutKey()
    {
        var authPath = CreateFile("config/auth.json", "{}");
        var providersPath = CreateFile("config/providers.json", "{\"openai\":{\"type\":\"quota-based\",\"base_url\":\"https://configured\"}}");

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

        var priorValue = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-env-key");

            var configs = await loader.LoadConfigAsync();

            var openAi = Assert.Single(configs, config => config.ProviderId == "openai");
            Assert.Equal("sk-env-key", openAi.ApiKey);
            Assert.Equal("https://configured", openAi.BaseUrl);
            Assert.Equal("quota-based", openAi.Type);
            Assert.Equal("Env: OPENAI_API_KEY", openAi.AuthSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", priorValue);
        }
    }
}
