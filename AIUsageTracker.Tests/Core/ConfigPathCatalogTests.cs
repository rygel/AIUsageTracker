// <copyright file="ConfigPathCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Paths;
using Moq;

namespace AIUsageTracker.Tests.Core;

public class ConfigPathCatalogTests
{
    [Fact]
    public void GetConfigEntries_ReturnsLegacyProviderAppAndCanonicalEntriesInPriorityOrder()
    {
        var pathProvider = CreatePathProvider(
            "C:\\test\\canonical\\auth.json",
            "C:\\test\\providers.json",
            "C:\\test\\appdata",
            "C:\\Users\\test");

        var entries = ConfigPathCatalog.GetConfigEntries(pathProvider.Object);

        Assert.Equal(7, entries.Count);
        Assert.Equal("C:\\Users\\test\\.local\\share\\opencode\\auth.json", entries[0].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[0].Kind);
        Assert.Equal("C:\\Users\\test\\.config\\opencode\\auth.json", entries[1].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[1].Kind);
        Assert.Equal("C:\\Users\\test\\AppData\\Roaming\\opencode\\auth.json", entries[2].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[2].Kind);
        Assert.Equal("C:\\Users\\test\\AppData\\Local\\opencode\\auth.json", entries[3].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[3].Kind);
        Assert.Equal("C:\\test\\providers.json", entries[4].Path);
        Assert.Equal(ConfigPathKind.Provider, entries[4].Kind);
        Assert.Equal("C:\\test\\appdata\\auth.json", entries[5].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[5].Kind);
        Assert.Equal("C:\\test\\canonical\\auth.json", entries[6].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[6].Kind);
    }

    [Fact]
    public void GetConfigEntries_DeduplicatesWhenAuthPathsMatch()
    {
        var pathProvider = CreatePathProvider(
            "C:\\test\\appdata\\auth.json",
            "C:\\test\\providers.json",
            "C:\\test\\appdata",
            "C:\\Users\\test");

        var entries = ConfigPathCatalog.GetConfigEntries(pathProvider.Object);

        Assert.Equal(6, entries.Count);
        Assert.Equal("C:\\Users\\test\\.local\\share\\opencode\\auth.json", entries[0].Path);
        Assert.Equal("C:\\Users\\test\\.config\\opencode\\auth.json", entries[1].Path);
        Assert.Equal("C:\\Users\\test\\AppData\\Roaming\\opencode\\auth.json", entries[2].Path);
        Assert.Equal("C:\\Users\\test\\AppData\\Local\\opencode\\auth.json", entries[3].Path);
        Assert.Equal("C:\\test\\providers.json", entries[4].Path);
        Assert.Equal("C:\\test\\appdata\\auth.json", entries[5].Path);
    }

    [Fact]
    public void GetConfigEntries_IncludesCanonicalAuthPathWhenAppDataRootMissing()
    {
        var pathProvider = CreatePathProvider(
            "C:\\test\\config\\auth.json",
            "C:\\test\\config\\providers.json",
            null,
            "C:\\Users\\test");

        var entries = ConfigPathCatalog.GetConfigEntries(pathProvider.Object);

        Assert.Equal(6, entries.Count);
        Assert.Equal("C:\\test\\config\\auth.json", entries[^1].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[^1].Kind);
    }

    private static Mock<IAppPathProvider> CreatePathProvider(
        string authPath,
        string providerPath,
        string? appDataRoot = null,
        string? userProfileRoot = "C:\\Users\\test")
    {
        var pathProvider = new Mock<IAppPathProvider>();
        pathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        pathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providerPath);
        pathProvider.Setup(p => p.GetAppDataRoot()).Returns(appDataRoot);
        pathProvider.Setup(p => p.GetUserProfileRoot()).Returns(userProfileRoot ?? string.Empty);
        return pathProvider;
    }
}
