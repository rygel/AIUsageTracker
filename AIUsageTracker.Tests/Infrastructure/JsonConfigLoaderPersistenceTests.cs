// <copyright file="JsonConfigLoaderPersistenceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure
{
    using System.Text.Json;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Infrastructure.Configuration;
    using Microsoft.Extensions.Logging.Abstractions;
    using Moq;

    public sealed class JsonConfigLoaderPersistenceTests : IntegrationTestBase
    {
        [Fact]
        public async Task SaveConfigAsync_DoesNotPersistNonPersistedProviderIds()
        {
            var authPath = this.CreateFile("config/auth.json", "{\"codex.spark\":{\"key\":\"legacy\"}}");
            var providersPath = this.CreateFile("config/providers.json", "{\"codex.spark\":{\"type\":\"quota-based\"}}");

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

        [Fact]
        public async Task LoadPreferencesAsync_PrefersCanonicalPreferencesFile_OverLegacyAuthSettings()
        {
            var authPath = this.CreateFile("config/auth.json", "{\"app_settings\":{\"Theme\":1}}");
            var providersPath = this.CreateFile("config/providers.json", "{}");
            var preferencesPath = this.CreateFile("config/preferences.json", "{\"Theme\":4}");

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
            mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
            mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(preferencesPath);
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
            mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
            mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
            mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

            var loader = new JsonConfigLoader(
                logger: NullLogger<JsonConfigLoader>.Instance,
                tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
                pathProvider: mockPathProvider.Object);

            var preferences = await loader.LoadPreferencesAsync();

            Assert.Equal(AppTheme.Dracula, preferences.Theme);
        }

        [Fact]
        public async Task SavePreferencesAsync_WritesCanonicalPreferencesFile_WithoutMutatingAuthJson()
        {
            var authPath = this.CreateFile("config/auth.json", "{\"app_settings\":{\"Theme\":1},\"codex\":{\"key\":\"abc\"}}");
            var providersPath = this.CreateFile("config/providers.json", "{}");
            var preferencesPath = Path.Combine(this.TestRootPath, "config", "preferences.json");

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
            mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
            mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(preferencesPath);
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
            mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
            mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
            mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

            var loader = new JsonConfigLoader(
                logger: NullLogger<JsonConfigLoader>.Instance,
                tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
                pathProvider: mockPathProvider.Object);

            await loader.SavePreferencesAsync(new AppPreferences { Theme = AppTheme.Nord });

            var savedPreferences = JsonSerializer.Deserialize<AppPreferences>(await File.ReadAllTextAsync(preferencesPath));
            var authJson = await File.ReadAllTextAsync(authPath);

            Assert.NotNull(savedPreferences);
            Assert.Equal(AppTheme.Nord, savedPreferences!.Theme);
            Assert.Contains("\"app_settings\":{\"Theme\":1}", authJson, StringComparison.Ordinal);
            Assert.Contains("\"codex\":{\"key\":\"abc\"}", authJson, StringComparison.Ordinal);
        }

        [Fact]
        public async Task LoadPreferencesAsync_DoesNotUseLegacyAuthSettings_WhenCanonicalPreferencesFileIsMissing()
        {
            var authPath = this.CreateFile("config/auth.json", "{\"app_settings\":{\"Theme\":1}}");
            var providersPath = this.CreateFile("config/providers.json", "{}");
            var preferencesPath = Path.Combine(this.TestRootPath, "config", "preferences.json");

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
            mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
            mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(preferencesPath);
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
            mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
            mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
            mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

            var loader = new JsonConfigLoader(
                logger: NullLogger<JsonConfigLoader>.Instance,
                tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
                pathProvider: mockPathProvider.Object);

            var preferences = await loader.LoadPreferencesAsync();

            Assert.Equal(new AppPreferences().Theme, preferences.Theme);
        }

        [Fact]
        public async Task LoadConfigAsync_ReadsLegacyProvidersFile_WhenCanonicalProvidersFileIsMissing()
        {
            var authPath = this.CreateFile("config/auth.json", "{}");
            var providersPath = Path.Combine(this.TestRootPath, "config", "providers.json");
            var legacyProvidersPath = this.CreateFile("AppData/Local/AIConsumptionTracker/providers.json", "{\"openai\":{\"type\":\"quota-based\",\"base_url\":\"https://legacy\"}}");

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
            mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
            mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(Path.Combine(this.TestRootPath, "config", "preferences.json"));
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
            mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(Path.Combine(this.TestRootPath, "config"));
            mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
            mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

            var loader = new JsonConfigLoader(
                logger: NullLogger<JsonConfigLoader>.Instance,
                tokenDiscoveryLogger: NullLogger<TokenDiscoveryService>.Instance,
                pathProvider: mockPathProvider.Object);

            var configs = await loader.LoadConfigAsync();

            var config = Assert.Single(configs.Where(c => c.ProviderId == "openai"));
            Assert.Equal("https://legacy", config.BaseUrl);
            Assert.Equal("quota-based", config.Type);
        }
    }
}
