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
    public void HasVisibleDerivedProviders_TreatsAntigravityAsVisibleChildProviderFamily()
    {
        var result = ProviderCapabilityCatalog.HasVisibleDerivedProviders("antigravity");

        Assert.True(result);
    }

    [Fact]
    public void ShouldShowInMainWindow_HidesLegacyOpenAiProvider()
    {
        var result = ProviderCapabilityCatalog.ShouldShowInMainWindow("openai");

        Assert.False(result);
    }

    [Fact]
    public void ResolveDisplayLabel_PreservesDerivedProviderName_WhenProvided()
    {
        var result = ProviderCapabilityCatalog.ResolveDisplayLabel(
            "antigravity.gpt-oss",
            "GPT OSS (Anti-Gravity)");

        Assert.Equal("GPT OSS (Anti-Gravity)", result);
    }

    [Fact]
    public void ResolveDisplayLabel_PreservesRuntimeName_ForGeminiDerivedProvider()
    {
        var result = ProviderCapabilityCatalog.ResolveDisplayLabel(
            "gemini-cli.minute",
            "Gemini 2.5 Flash Lite [Gemini CLI]");

        Assert.Equal("Gemini 2.5 Flash Lite [Gemini CLI]", result);
    }

    [Fact]
    public void GetConfiguredDisplayName_UsesMetadataLabelForFamilyChildWithoutRuntimeLabel()
    {
        var result = ProviderCapabilityCatalog.GetConfiguredDisplayName("antigravity.gpt-oss");

        Assert.Equal("Google Antigravity", result);
    }

    [Fact]
    public void SupportsAccountIdentity_UsesProviderMetadata()
    {
        var result = ProviderCapabilityCatalog.SupportsAccountIdentity("github-copilot");

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseSharedSubDetailCollapsePreference_DelegatesToProviderMetadata()
    {
        var result = ProviderCapabilityCatalog.ShouldUseSharedSubDetailCollapsePreference("codex.spark");

        Assert.False(result);
    }

    [Fact]
    public void ShouldRenderAsSettingsSubItem_DelegatesToProviderMetadata()
    {
        var result = ProviderCapabilityCatalog.ShouldRenderAsSettingsSubItem("antigravity.some-model");

        Assert.False(result);
    }
}
