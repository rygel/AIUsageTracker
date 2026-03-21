// <copyright file="VersionDisplayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class VersionDisplayCatalogTests
{
    [Fact]
    public void Create_WithoutPrereleaseLabel_UsesCoreOnly()
    {
        var presentation = VersionDisplayCatalog.Create("2.7.0", null);

        Assert.Equal("v2.7.0", presentation.DisplayVersion);
        Assert.Equal("AI Usage Tracker v2.7.0", presentation.WindowTitle);
    }

    [Fact]
    public void Create_WithPrereleaseLabel_AppendsSuffix()
    {
        var presentation = VersionDisplayCatalog.Create("2.7.0", "Beta 6");

        Assert.Equal("v2.7.0 Beta 6", presentation.DisplayVersion);
        Assert.Equal("AI Usage Tracker v2.7.0 Beta 6", presentation.WindowTitle);
    }

    [Fact]
    public void ParsePrereleaseLabel_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(VersionDisplayCatalog.ParsePrereleaseLabel(null));
        Assert.Null(VersionDisplayCatalog.ParsePrereleaseLabel(string.Empty));
        Assert.Null(VersionDisplayCatalog.ParsePrereleaseLabel(" "));
    }

    [Fact]
    public void ParsePrereleaseLabel_WithoutDash_ReturnsNull()
    {
        var result = VersionDisplayCatalog.ParsePrereleaseLabel("2.7.0+abcdef");

        Assert.Null(result);
    }

    [Fact]
    public void ParsePrereleaseLabel_BetaLabel_FormatsNumber()
    {
        var result = VersionDisplayCatalog.ParsePrereleaseLabel("2.7.0-beta.6+abcdef");

        Assert.Equal("Beta 6", result);
    }

    [Fact]
    public void ParsePrereleaseLabel_AlphaLabel_FormatsNumber()
    {
        var result = VersionDisplayCatalog.ParsePrereleaseLabel("2.7.0-alpha.2");

        Assert.Equal("Alpha 2", result);
    }

    [Fact]
    public void ParsePrereleaseLabel_RcLabel_FormatsNumber()
    {
        var result = VersionDisplayCatalog.ParsePrereleaseLabel("2.7.0-rc.1");

        Assert.Equal("RC 1", result);
    }

    [Fact]
    public void ParsePrereleaseLabel_CustomLabel_ReplacesDots()
    {
        var result = VersionDisplayCatalog.ParsePrereleaseLabel("2.7.0-preview.fast");

        Assert.Equal("preview fast", result);
    }
}
