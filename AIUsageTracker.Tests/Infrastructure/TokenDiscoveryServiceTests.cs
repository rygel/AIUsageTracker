// <copyright file="TokenDiscoveryServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Infrastructure.Configuration;
    using Microsoft.Extensions.Logging.Abstractions;
    using Moq;
    using System.Text.Json;

    public class TokenDiscoveryServiceTests
    {
        [Fact]
        public async Task DiscoverTokensAsync_IncludesCodexAsWellKnownProvider()
        {
            // Arrange
            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(Path.GetTempPath());
            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

            // Act
            var configs = await discovery.DiscoverTokensAsync();

            // Assert
            var codex = configs.FirstOrDefault(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(codex);
            Assert.Equal(PlanType.Coding, codex!.PlanType);
            Assert.Equal("quota-based", codex.Type);
        }

        [Fact]
        public async Task DiscoverTokensAsync_DoesNotExpandWellKnownProviderAliases()
        {
            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(Path.GetTempPath());
            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

            var configs = await discovery.DiscoverTokensAsync();

            Assert.DoesNotContain(configs, c => c.ProviderId.Equals("minimax-io", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(configs, c => c.ProviderId.Equals("minimax-global", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(configs, c => c.ProviderId.Equals("kimi-for-coding", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task DiscoverTokensAsync_DoesNotReadProvidersConfigAsTokenSource()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), $"token-discovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(testRoot);

            try
            {
                var providerConfigPath = Path.Combine(testRoot, "providers.json");
                var json = JsonSerializer.Serialize(new
                {
                    openai = new
                    {
                        key = "config-only-key",
                    },
                });
                await File.WriteAllTextAsync(providerConfigPath, json);

                var mockPathProvider = new Mock<IAppPathProvider>();
                mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);
                mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providerConfigPath);

                var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

                var configs = await discovery.DiscoverTokensAsync();

                var openAi = configs.FirstOrDefault(c => c.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase));
                Assert.True(
                    openAi == null ||
                    string.IsNullOrEmpty(openAi.ApiKey) ||
                    !string.Equals(openAi.ApiKey, "config-only-key", StringComparison.Ordinal),
                    "Token discovery should not read providers.json as an auth source.");
            }
            finally
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }

        [Fact]
        public async Task DiscoverTokensAsync_DoesNotReadCanonicalAuthConfigAsSessionTokenSource()
        {
            var testRoot = Path.Combine(Path.GetTempPath(), $"token-discovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(testRoot);

            try
            {
                var authConfigPath = Path.Combine(testRoot, "auth.json");
                var json = JsonSerializer.Serialize(new
                {
                    openai = new
                    {
                        access = "config-file-session-token",
                    },
                });
                await File.WriteAllTextAsync(authConfigPath, json);

                var mockPathProvider = new Mock<IAppPathProvider>();
                mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);
                mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authConfigPath);

                var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

                var configs = await discovery.DiscoverTokensAsync();

                var codex = configs.FirstOrDefault(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase));
                Assert.True(codex == null || string.IsNullOrEmpty(codex.ApiKey));
            }
            finally
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
