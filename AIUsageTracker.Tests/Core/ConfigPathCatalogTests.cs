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
    public void GetConfigEntries_ReturnsAuthThenProviderEntries()
    {
        var pathProvider = CreatePathProvider("C:\\test\\auth.json", "C:\\test\\providers.json");

        var entries = ConfigPathCatalog.GetConfigEntries(pathProvider.Object);

        Assert.Equal(2, entries.Count);
        Assert.Equal("C:\\test\\auth.json", entries[0].Path);
        Assert.True(entries[0].IsAuthFile);
        Assert.Equal(ConfigPathKind.Auth, entries[0].Kind);
        Assert.Equal("C:\\test\\providers.json", entries[1].Path);
        Assert.False(entries[1].IsAuthFile);
        Assert.Equal(ConfigPathKind.Provider, entries[1].Kind);
    }

    [Fact]
    public void LegacyPathHelpers_ReturnExpectedValues()
    {
        var pathProvider = CreatePathProvider("C:\\test\\auth.json", "C:\\test\\providers.json");

        var authPaths = ConfigPathCatalog.GetAuthConfigPaths(pathProvider.Object);
        var providerPaths = ConfigPathCatalog.GetProviderConfigPaths(pathProvider.Object);

        Assert.Single(authPaths);
        Assert.Equal("C:\\test\\auth.json", authPaths[0]);
        Assert.Single(providerPaths);
        Assert.Equal("C:\\test\\providers.json", providerPaths[0]);
    }

    private static Mock<IAppPathProvider> CreatePathProvider(string authPath, string providerPath)
    {
        var pathProvider = new Mock<IAppPathProvider>();
        pathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        pathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providerPath);
        return pathProvider;
    }
}
