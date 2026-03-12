// <copyright file="ProviderAccountDisplayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderAccountDisplayCatalogTests
{
    [Fact]
    public void ResolveDisplayAccountName_UsesUsageAccountName_WhenPresent()
    {
        var result = ProviderAccountDisplayCatalog.ResolveDisplayAccountName(
            providerId: "github-copilot",
            usageAccountName: "actual-gh-user",
            isPrivacyMode: false);

        Assert.Equal("actual-gh-user", result);
    }

    [Fact]
    public void ResolveDisplayAccountName_ReturnsEmpty_WhenUsageAccountMissing()
    {
        var result = ProviderAccountDisplayCatalog.ResolveDisplayAccountName(
            providerId: "github-copilot",
            usageAccountName: null,
            isPrivacyMode: false);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveDisplayAccountName_ReturnsEmpty_ForNonIdentityProvider()
    {
        var result = ProviderAccountDisplayCatalog.ResolveDisplayAccountName(
            providerId: "synthetic",
            usageAccountName: "should-not-render",
            isPrivacyMode: false);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveDisplayAccountName_MasksIdentity_WhenPrivacyModeEnabled()
    {
        var result = ProviderAccountDisplayCatalog.ResolveDisplayAccountName(
            providerId: "github-copilot",
            usageAccountName: "github-user@example.com",
            isPrivacyMode: true);

        Assert.NotEqual("github-user@example.com", result);
        Assert.Contains("@", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDisplayAccountName_DoesNotInferIdentitySupport_ForUnknownDerivedId()
    {
        var result = ProviderAccountDisplayCatalog.ResolveDisplayAccountName(
            providerId: "github-copilot.enterprise",
            usageAccountName: "octocat",
            isPrivacyMode: false);

        Assert.Equal(string.Empty, result);
    }
}
