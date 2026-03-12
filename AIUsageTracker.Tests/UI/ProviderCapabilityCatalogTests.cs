// <copyright file="ProviderCapabilityCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderCapabilityCatalogTests
{
    [Fact]
    public void ShouldShowInSettings_UsesProviderMetadata()
    {
        var result = ProviderCapabilityCatalog.ShouldShowInSettings("codex");

        Assert.True(result);
    }

    [Fact]
    public void GetCanonicalProviderId_UsesProviderMetadata()
    {
        var canonical = ProviderCapabilityCatalog.GetCanonicalProviderId(
            "antigravity.gpt-5");

        Assert.Equal("antigravity", canonical);
    }

    [Fact]
    public void GetDefaultSettingsProviderIds_UsesProviderMetadata()
    {
        var providerIds = ProviderCapabilityCatalog.GetDefaultSettingsProviderIds();

        Assert.Contains("codex", providerIds);
        Assert.Contains("codex.spark", providerIds);
    }

    [Fact]
    public void GetDisplayName_PreservesDerivedProviderName_WhenProvided()
    {
        var result = ProviderCapabilityCatalog.GetDisplayName(
            "antigravity.gpt-oss",
            "GPT OSS (Anti-Gravity)");

        Assert.Equal("GPT OSS (Anti-Gravity)", result);
    }

    [Fact]
    public void GetDisplayName_PreservesRuntimeName_ForGeminiDerivedProvider()
    {
        var result = ProviderCapabilityCatalog.GetDisplayName(
            "gemini-cli.minute",
            "Gemini 2.5 Flash Lite [Gemini CLI]");

        Assert.Equal("Gemini 2.5 Flash Lite [Gemini CLI]", result);
    }

    [Fact]
    public void SupportsAccountIdentity_UsesProviderMetadata()
    {
        var result = ProviderCapabilityCatalog.SupportsAccountIdentity("github-copilot");

        Assert.True(result);
    }
}
