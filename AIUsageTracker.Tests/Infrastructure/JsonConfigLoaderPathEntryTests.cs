// <copyright file="JsonConfigLoaderPathEntryTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Configuration;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public class JsonConfigLoaderPathEntryTests
{
    [Fact]
    public void BuildConfigEntries_ReturnsLegacyProviderAppAndCanonicalEntriesInPriorityOrder()
    {
        var pathProvider = CreatePathProvider(
            "C:\\test\\canonical\\auth.json",
            "C:\\test\\providers.json",
            "C:\\test\\appdata",
            "C:\\Users\\test");

        var entries = JsonConfigLoader.BuildConfigEntries(pathProvider.Object);

        Assert.Equal(8, entries.Count);
        Assert.Equal("C:\\Users\\test\\.opencode\\auth.json", entries[0].Path);
        Assert.True(entries[0].IsAuthFile);
        Assert.Equal("C:\\Users\\test\\.config\\opencode\\auth.json", entries[1].Path);
        Assert.True(entries[1].IsAuthFile);
        Assert.Equal("C:\\Users\\test\\AppData\\Roaming\\opencode\\auth.json", entries[2].Path);
        Assert.True(entries[2].IsAuthFile);
        Assert.Equal("C:\\Users\\test\\AppData\\Local\\opencode\\auth.json", entries[3].Path);
        Assert.True(entries[3].IsAuthFile);
        Assert.Equal("C:\\Users\\test\\.local\\share\\opencode\\auth.json", entries[4].Path);
        Assert.True(entries[4].IsAuthFile);
        Assert.Equal("C:\\test\\providers.json", entries[5].Path);
        Assert.False(entries[5].IsAuthFile);
        Assert.Equal("C:\\test\\appdata\\auth.json", entries[6].Path);
        Assert.True(entries[6].IsAuthFile);
        Assert.Equal("C:\\test\\canonical\\auth.json", entries[7].Path);
        Assert.True(entries[7].IsAuthFile);
    }

    [Fact]
    public void BuildConfigEntries_DeduplicatesWhenAuthPathsMatch()
    {
        var pathProvider = CreatePathProvider(
            "C:\\test\\appdata\\auth.json",
            "C:\\test\\providers.json",
            "C:\\test\\appdata",
            "C:\\Users\\test");

        var entries = JsonConfigLoader.BuildConfigEntries(pathProvider.Object);

        Assert.Equal(7, entries.Count);
        Assert.Equal("C:\\Users\\test\\.opencode\\auth.json", entries[0].Path);
        Assert.Equal("C:\\Users\\test\\.config\\opencode\\auth.json", entries[1].Path);
        Assert.Equal("C:\\Users\\test\\AppData\\Roaming\\opencode\\auth.json", entries[2].Path);
        Assert.Equal("C:\\Users\\test\\AppData\\Local\\opencode\\auth.json", entries[3].Path);
        Assert.Equal("C:\\Users\\test\\.local\\share\\opencode\\auth.json", entries[4].Path);
        Assert.Equal("C:\\test\\providers.json", entries[5].Path);
        Assert.Equal("C:\\test\\appdata\\auth.json", entries[6].Path);
    }

    [Fact]
    public void BuildConfigEntries_IncludesCanonicalAuthPathWhenAppDataRootMissing()
    {
        var pathProvider = CreatePathProvider(
            "C:\\test\\config\\auth.json",
            "C:\\test\\config\\providers.json",
            null,
            "C:\\Users\\test");

        var entries = JsonConfigLoader.BuildConfigEntries(pathProvider.Object);

        Assert.Equal(7, entries.Count);
        Assert.Equal("C:\\test\\config\\auth.json", entries[^1].Path);
        Assert.True(entries[^1].IsAuthFile);
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
        pathProvider.Setup(p => p.GetAppDataRoot()).Returns(appDataRoot!);
        pathProvider.Setup(p => p.GetUserProfileRoot()).Returns(userProfileRoot ?? string.Empty);
        return pathProvider;
    }
}
