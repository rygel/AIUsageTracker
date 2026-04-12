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

        var snapshot = await service.GetGroupedUsageAsync().ConfigureAwait(false);

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

        var snapshot = await service.GetGroupedUsageAsync().ConfigureAwait(false);

        Assert.Contains(snapshot.Providers, p =>
            string.Equals(p.ProviderId, "openrouter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetGroupedUsage_SubProvider_WithKey_IsNotHiddenByUnconfiguredSibling()
    {
        // Regression: the canonical-ID filter incorrectly hid all sub-providers of a
        // family when any sibling had an empty key. E.g. minimax (China, no key) and
        // minimax-io (International, has key) share canonical ID "minimax". The old code
        // put "minimax" in the exclusion set and then checked GetCanonicalProviderId on
        // each usage row — so minimax-io usage (also canonical "minimax") was filtered out
        // even though the user had a key configured for it.
        //
        // Note: GroupedUsageProjectionService.Build groups by canonical ID, so the output
        // snapshot has ProviderId = "minimax" (the canonical) whether the data came from
        // minimax or minimax-io. The key assertion is that the MiniMax family appears and
        // reflects the configured (minimax-io) data — i.e. IsAvailable = true — not the
        // "API Key not found" data from the unconfigured minimax entry.
        var dbUsages = new List<ProviderUsage>
        {
            new() { ProviderId = "minimax", ProviderName = "MiniMax.chat", IsAvailable = false, Description = "API Key not found." },
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

        var snapshot = await service.GetGroupedUsageAsync().ConfigureAwait(false);

        // The MiniMax family (canonical "minimax") must appear — minimax-io has a key.
        var minimaxGroup = snapshot.Providers
            .FirstOrDefault(p => string.Equals(p.ProviderId, "minimax", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(minimaxGroup);

        // The group must reflect the minimax-io data (available, 30% used),
        // not the "API Key not found" data from the unconfigured minimax entry.
        Assert.True(minimaxGroup.IsAvailable,
            "MiniMax group should be available because minimax-io has a key; " +
            "the unconfigured minimax entry must have been excluded before Build.");
    }
}
