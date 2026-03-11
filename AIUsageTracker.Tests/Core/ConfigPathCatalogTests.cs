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
    public void GetConfigEntries_ReturnsAuthProviderThenAppAuthEntries()
    {
        var pathProvider = CreatePathProvider("C:\\test\\auth.json", "C:\\test\\providers.json", "C:\\test\\appdata");

        var entries = ConfigPathCatalog.GetConfigEntries(pathProvider.Object);

        Assert.Equal(3, entries.Count);
        Assert.Equal("C:\\test\\auth.json", entries[0].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[0].Kind);
        Assert.Equal("C:\\test\\providers.json", entries[1].Path);
        Assert.Equal(ConfigPathKind.Provider, entries[1].Kind);
        Assert.Equal("C:\\test\\appdata\\auth.json", entries[2].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[2].Kind);
    }

    [Fact]
    public void GetConfigEntries_DeduplicatesWhenAuthPathsMatch()
    {
        var pathProvider = CreatePathProvider("C:\\test\\appdata\\auth.json", "C:\\test\\providers.json", "C:\\test\\appdata");

        var entries = ConfigPathCatalog.GetConfigEntries(pathProvider.Object);

        Assert.Equal(2, entries.Count);
        Assert.Equal("C:\\test\\appdata\\auth.json", entries[0].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[0].Kind);
        Assert.Equal("C:\\test\\providers.json", entries[1].Path);
        Assert.Equal(ConfigPathKind.Provider, entries[1].Kind);
    }

    [Fact]
    public void GetConfigEntries_IncludesLegacyAuthPathWhenAppDataRootExists()
    {
        var pathProvider = CreatePathProvider(
            "C:\\test\\config\\auth.json",
            "C:\\test\\config\\providers.json",
            "C:\\test\\appdata");

        var entries = ConfigPathCatalog.GetConfigEntries(pathProvider.Object);

        Assert.Equal(3, entries.Count);
        Assert.Equal("C:\\test\\appdata\\auth.json", entries[2].Path);
        Assert.Equal(ConfigPathKind.Auth, entries[2].Kind);
    }

    private static Mock<IAppPathProvider> CreatePathProvider(string authPath, string providerPath, string? appDataRoot = null)
    {
        var pathProvider = new Mock<IAppPathProvider>();
        pathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        pathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providerPath);
        pathProvider.Setup(p => p.GetAppDataRoot()).Returns(appDataRoot);
        return pathProvider;
    }
}
