// <copyright file="ProviderUsagePersistenceServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderUsagePersistenceServiceTests
{
    private readonly Mock<IUsageDatabase> _database = new();

    [Fact]
    public async Task PersistUsageAndDynamicProvidersAsync_DoesNotRegisterKnownRootProviderAsync()
    {
        var service = this.CreateService();
        var activeProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "openai" };
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                AuthSource = "test",
            },
        };

        await service.PersistUsageAndDynamicProvidersAsync(usages, activeProviderIds);

        this._database.Verify(
            database => database.StoreProviderAsync(It.IsAny<ProviderConfig>(), It.IsAny<string?>()),
            Times.Never);
        this._database.Verify(database => database.StoreHistoryAsync(usages), Times.Once);
    }

    [Fact]
    public async Task PersistUsageAndDynamicProvidersAsync_RegistersDynamicChildProviderAsync()
    {
        var service = this.CreateService();
        var activeProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "antigravity" };
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "antigravity.gemini-pro",
                ProviderName = "Gemini Pro",
                AuthSource = "session",
                IsQuotaBased = true,
            },
        };

        await service.PersistUsageAndDynamicProvidersAsync(usages, activeProviderIds);

        this._database.Verify(
            database => database.StoreProviderAsync(
                It.Is<ProviderConfig>(config =>
                    config.ProviderId == "antigravity.gemini-pro" &&
                    config.AuthSource == "session" &&
                    config.ApiKey == "dynamic"),
                "Gemini Pro"),
            Times.Once);
        Assert.Contains("antigravity.gemini-pro", activeProviderIds);
    }

    [Fact]
    public async Task PersistUsageAndDynamicProvidersAsync_RegistersDynamicChildProvider_WhenActiveProviderUsesAliasAsync()
    {
        var service = this.CreateService();
        var activeProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gemini" };
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "gemini-cli.hourly",
                ProviderName = "Gemini CLI (Hourly)",
                AuthSource = "session",
                IsQuotaBased = true,
            },
        };

        await service.PersistUsageAndDynamicProvidersAsync(usages, activeProviderIds);

        this._database.Verify(
            database => database.StoreProviderAsync(
                It.Is<ProviderConfig>(config =>
                    config.ProviderId == "gemini-cli.hourly" &&
                    config.AuthSource == "session" &&
                    config.ApiKey == "dynamic"),
                "Gemini CLI (Hourly)"),
            Times.Once);
        Assert.Contains("gemini-cli.hourly", activeProviderIds);
    }

    [Fact]
    public async Task PersistUsageAndDynamicProvidersAsync_StoresHistoryAndOnlyNonEmptyRawSnapshotsAsync()
    {
        var service = this.CreateService();
        var activeProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "dynamic-one",
                ProviderName = "Dynamic One",
                AuthSource = "test",
                RawJson = "{\"ok\":true}",
                HttpStatus = 201,
            },
            new()
            {
                ProviderId = "dynamic-two",
                ProviderName = "Dynamic Two",
                AuthSource = "test",
                RawJson = string.Empty,
                HttpStatus = 200,
            },
        };

        await service.PersistUsageAndDynamicProvidersAsync(usages, activeProviderIds);

        this._database.Verify(database => database.StoreHistoryAsync(usages), Times.Once);
        this._database.Verify(
            database => database.StoreRawSnapshotAsync("dynamic-one", "{\"ok\":true}", 201),
            Times.Once);
        this._database.Verify(
            database => database.StoreRawSnapshotAsync("dynamic-two", It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    private ProviderUsagePersistenceService CreateService()
    {
        return new ProviderUsagePersistenceService(this._database.Object, NullLogger<ProviderUsagePersistenceService>.Instance);
    }
}
