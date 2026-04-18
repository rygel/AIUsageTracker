// <copyright file="CachedGroupedUsageProjectionServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Moq;

namespace AIUsageTracker.Tests.Services;

public sealed class CachedGroupedUsageProjectionServiceTests
{
    [Fact]
    public async Task GetGroupedUsage_ExcludesUnconfiguredStandardApiKeyProviders_FromSnapshot()
    {
        // Providers that are StandardApiKey mode with no API key (e.g. OpenRouter, Xiaomi when
        // unconfigured) must not appear in the main-window snapshot even if they have stale
        // history rows in the database, because the database never persists State and those
        // rows always deserialise with State=Available.
        var dbUsages = new List<ProviderUsage>
        {
            // openrouter row with no state info (as it comes from the DB)
            new() { ProviderId = "openrouter", ProviderName = "OpenRouter", IsAvailable = false, Description = "API Key missing." },

            // A configured provider that should appear
            new() { ProviderId = "mistral", ProviderName = "Mistral", IsAvailable = true, UsedPercent = 20 },
        };

        var configs = new List<ProviderConfig>
        {
            // openrouter has no API key → unconfigured StandardApiKey provider
            new() { ProviderId = "openrouter", ApiKey = string.Empty },

            // mistral has a key → should appear
            new() { ProviderId = "mistral", ApiKey = "sk-test-key" },
        };

        var mockDb = new Mock<IUsageDatabase>();
        mockDb.Setup(d => d.GetLatestHistoryAsync(It.IsAny<IReadOnlyCollection<string>?>()))
            .Returns((IReadOnlyCollection<string>? ids) => Task.FromResult<IReadOnlyList<ProviderUsage>>(
                ids == null ? dbUsages : dbUsages
                    .Where(u => ids.Contains(u.ProviderId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    .ToList()));

        var mockConfig = new Mock<IConfigService>();
        mockConfig.Setup(c => c.GetConfigsAsync()).ReturnsAsync(configs);

        var service = new CachedGroupedUsageProjectionService(mockDb.Object, mockConfig.Object);

        var snapshot = await service.GetGroupedUsageAsync();

        // OpenRouter must not be in the snapshot
        Assert.DoesNotContain(snapshot.Providers, p =>
            string.Equals(p.ProviderId, "openrouter", StringComparison.OrdinalIgnoreCase));

        // Mistral must still be present
        Assert.Contains(snapshot.Providers, p =>
            string.Equals(p.ProviderId, "mistral", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetGroupedUsage_IncludesStandardApiKeyProviders_WhenApiKeyIsConfigured()
    {
        // A StandardApiKey provider WITH a key should appear in the snapshot normally.
        var dbUsages = new List<ProviderUsage>
        {
            new() { ProviderId = "openrouter", ProviderName = "OpenRouter", IsAvailable = true, UsedPercent = 50 },
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openrouter", ApiKey = "sk-or-real-key" },
        };

        var mockDb = new Mock<IUsageDatabase>();
        mockDb.Setup(d => d.GetLatestHistoryAsync(It.IsAny<IReadOnlyCollection<string>?>()))
            .Returns((IReadOnlyCollection<string>? ids) => Task.FromResult<IReadOnlyList<ProviderUsage>>(
                ids == null ? dbUsages : dbUsages
                    .Where(u => ids.Contains(u.ProviderId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    .ToList()));

        var mockConfig = new Mock<IConfigService>();
        mockConfig.Setup(c => c.GetConfigsAsync()).ReturnsAsync(configs);

        var service = new CachedGroupedUsageProjectionService(mockDb.Object, mockConfig.Object);

        var snapshot = await service.GetGroupedUsageAsync();

        Assert.Contains(snapshot.Providers, p =>
            string.Equals(p.ProviderId, "openrouter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetGroupedUsage_SubProvider_WithKey_IsNotHiddenByUnconfiguredSibling()
    {
        // Regression: filtering must evaluate each configured provider independently.
        // minimax (China, no key) must be excluded while minimax-io (International, has key)
        // remains visible.
        var dbUsages = new List<ProviderUsage>
        {
            new() { ProviderId = "minimax", ProviderName = "MiniMax.com", IsAvailable = false, Description = "API Key not found." },
            new() { ProviderId = "minimax-io", ProviderName = "MiniMax.io", IsAvailable = true, UsedPercent = 30 },
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "minimax", ApiKey = string.Empty },         // no key — usage row must be excluded
            new() { ProviderId = "minimax-io", ApiKey = "sk-minimax-key" },  // has key — usage row must survive
        };

        var mockDb = new Mock<IUsageDatabase>();
        mockDb.Setup(d => d.GetLatestHistoryAsync(It.IsAny<IReadOnlyCollection<string>?>()))
            .Returns((IReadOnlyCollection<string>? ids) => Task.FromResult<IReadOnlyList<ProviderUsage>>(
                ids == null ? dbUsages : dbUsages
                    .Where(u => ids.Contains(u.ProviderId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    .ToList()));

        var mockConfig = new Mock<IConfigService>();
        mockConfig.Setup(c => c.GetConfigsAsync()).ReturnsAsync(configs);

        var service = new CachedGroupedUsageProjectionService(mockDb.Object, mockConfig.Object);

        var snapshot = await service.GetGroupedUsageAsync();

        // The configured MiniMax.io provider must appear.
        var minimaxGroup = snapshot.Providers
            .FirstOrDefault(p => string.Equals(p.ProviderId, "minimax-io", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(minimaxGroup);

        // The group must reflect minimax-io data and remain available.
        Assert.True(
            minimaxGroup.IsAvailable,
            "MiniMax.io should be available because minimax-io has a key.");

        Assert.DoesNotContain(snapshot.Providers, p =>
            string.Equals(p.ProviderId, "minimax", StringComparison.OrdinalIgnoreCase));
    }
}
