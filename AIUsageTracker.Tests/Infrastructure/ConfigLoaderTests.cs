// <copyright file="ConfigLoaderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public class ConfigLoaderTests : IntegrationTestBase
{
    [Fact]
    public async Task LoadConfigAsync_CanonicalizesHandledProviderAliasesAsync()
    {
        var authPath = this.CreateFile("config/auth.json", "{\"kimi-for-coding\":{\"key\":\"kimi-test-key\",\"type\":\"quota-based\"}}");
        var providersPath = this.CreateFile("config/providers.json", "{}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(Path.Combine(this.TestRootPath, "preferences.json"));
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        var loader = new JsonConfigLoader(
            logger: NullLogger<JsonConfigLoader>.Instance,
            tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var configs = await loader.LoadConfigAsync();

        var kimi = Assert.Single(configs, config => string.Equals(config.ProviderId, "kimi", StringComparison.Ordinal));
        Assert.Equal("kimi-test-key", kimi.ApiKey);
        Assert.DoesNotContain(configs, config => string.Equals(config.ProviderId, "kimi-for-coding", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadConfigAsync_PreservesConfiguredApiKey_WhenDiscoveryFindsEnvironmentKeyAsync()
    {
        var authPath = this.CreateFile("config/auth.json", "{\"openai\":{\"key\":\"sk-configured-key\"}}");
        var providersPath = this.CreateFile("config/providers.json", "{}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(Path.Combine(this.TestRootPath, "preferences.json"));
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        var loader = new JsonConfigLoader(
            logger: NullLogger<JsonConfigLoader>.Instance,
            tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var priorValue = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-env-key");

            var configs = await loader.LoadConfigAsync();

            var openAi = Assert.Single(configs, config => string.Equals(config.ProviderId, "openai", StringComparison.Ordinal));
            Assert.Equal("sk-configured-key", openAi.ApiKey);
            Assert.Equal("Config: auth.json", openAi.AuthSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", priorValue);
        }
    }

    [Fact]
    public async Task LoadConfigAsync_UsesDiscoveryKey_ForExistingProviderSettingsWithoutKeyAsync()
    {
        var authPath = this.CreateFile("config/auth.json", "{}");
        var providersPath = this.CreateFile("config/providers.json", "{\"openai\":{\"type\":\"quota-based\",\"base_url\":\"https://configured\"}}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(Path.Combine(this.TestRootPath, "preferences.json"));
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        var loader = new JsonConfigLoader(
            logger: NullLogger<JsonConfigLoader>.Instance,
            tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var priorValue = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-env-key");

            var configs = await loader.LoadConfigAsync();

            var openAi = Assert.Single(configs, config => string.Equals(config.ProviderId, "openai", StringComparison.Ordinal));
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

    [Fact]
    public async Task LoadConfigAsync_CanonicalAuthFileOverridesEarlierAuthSourceAsync()
    {
        var authPath = this.CreateFile("external/auth.json", "{\"synthetic\":{\"key\":\"external-key\"}}");
        var providersPath = this.CreateFile("config/providers.json", "{}");
        var appDataRoot = Path.Combine(this.TestRootPath, "appdata");
        this.CreateFile(Path.Combine("appdata", "auth.json"), "{\"synthetic\":{\"key\":\"app-owned-key\"}}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(Path.Combine(this.TestRootPath, "preferences.json"));
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(appDataRoot);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        var loader = new JsonConfigLoader(
            logger: NullLogger<JsonConfigLoader>.Instance,
            tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var configs = await loader.LoadConfigAsync();

        var synthetic = Assert.Single(configs, config => string.Equals(config.ProviderId, "synthetic", StringComparison.Ordinal));
        Assert.Equal("external-key", synthetic.ApiKey);
    }

    [Fact]
    public async Task LoadConfigAsync_UsesLegacyOpenCodeAuthWhenCanonicalAuthIsEmptyAsync()
    {
        var canonicalAuthPath = this.CreateFile("home/.opencode/auth.json", "{\"synthetic\":{\"key\":\"\"}}");
        var providersPath = this.CreateFile("config/providers.json", "{}");
        this.CreateFile("home/.local/share/opencode/auth.json", "{\"synthetic\":{\"key\":\"legacy-shared-key\"}}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(canonicalAuthPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(Path.Combine(this.TestRootPath, "home"));
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(Path.Combine(this.TestRootPath, "preferences.json"));
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(Path.Combine(this.TestRootPath, "appdata"));
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        var loader = new JsonConfigLoader(
            logger: NullLogger<JsonConfigLoader>.Instance,
            tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var configs = await loader.LoadConfigAsync();

        var synthetic = Assert.Single(configs, config => string.Equals(config.ProviderId, "synthetic", StringComparison.Ordinal));
        Assert.Equal("legacy-shared-key", synthetic.ApiKey);
    }

    [Fact]
    public async Task LoadConfigAsync_IgnoresUnknownProviderIds_InStrictCatalogModeAsync()
    {
        var authPath = this.CreateFile("config/auth.json", "{\"anthropic\":{\"key\":\"legacy\"},\"codex\":{\"key\":\"codex-key\"}}");
        var providersPath = this.CreateFile("config/providers.json", "{}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(Path.Combine(this.TestRootPath, "preferences.json"));
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        var loader = new JsonConfigLoader(
            logger: NullLogger<JsonConfigLoader>.Instance,
            tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var configs = await loader.LoadConfigAsync();

        Assert.Contains(configs, config => string.Equals(config.ProviderId, "codex", StringComparison.Ordinal));
        Assert.DoesNotContain(configs, config => string.Equals(config.ProviderId, "anthropic", StringComparison.Ordinal));
    }
}
