// <copyright file="ProviderCapabilityCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderCapabilityCatalogTests
{
    [Fact]
    public void ShouldShowInSettings_UsesProviderMetadata()
    {
        var result = ProviderMetadataCatalog.ShouldShowInSettings("codex");

        Assert.True(result);
    }

    [Fact]
    public void GetCanonicalProviderId_UsesProviderMetadata()
    {
        var canonical = ProviderMetadataCatalog.GetCanonicalProviderId(
            "antigravity.gpt-5");

        Assert.Equal("antigravity", canonical);
    }

    [Fact]
    public void GetDefaultSettingsProviderIds_UsesProviderMetadata()
    {
        var providerIds = ProviderMetadataCatalog.GetDefaultSettingsProviderIds();

        Assert.Contains("codex", providerIds);
        Assert.Contains("codex.spark", providerIds);
    }

    [Fact]
    public void HasVisibleDerivedProviders_TreatsAntigravityAsVisibleChildProviderFamily()
    {
        var result = ProviderMetadataCatalog.HasDisplayableDerivedProviders("antigravity");

        Assert.True(result);
    }

    [Fact]
    public void ShouldShowInMainWindow_HidesLegacyOpenAiProvider()
    {
        var result = ProviderMetadataCatalog.ShouldShowInMainWindow("openai");

        Assert.False(result);
    }

    [Fact]
    public void ResolveDisplayLabel_PreservesDerivedProviderName_WhenProvided()
    {
        var result = ProviderMetadataCatalog.ResolveDisplayLabel(
            "antigravity.gpt-oss",
            "GPT OSS (Anti-Gravity)");

        Assert.Equal("GPT OSS (Anti-Gravity)", result);
    }

    [Fact]
    public void ResolveDisplayLabel_PreservesRuntimeName_ForGeminiDerivedProvider()
    {
        var result = ProviderMetadataCatalog.ResolveDisplayLabel(
            "gemini-cli.minute",
            "Gemini 2.5 Flash Lite [Gemini CLI]");

        Assert.Equal("Gemini 2.5 Flash Lite [Gemini CLI]", result);
    }

    [Fact]
    public void GetConfiguredDisplayName_UsesMetadataLabelForFamilyChildWithoutRuntimeLabel()
    {
        var result = ProviderMetadataCatalog.GetConfiguredDisplayName("antigravity.gpt-oss");

        Assert.Equal("Google Antigravity", result);
    }

    [Fact]
    public void SupportsAccountIdentity_UsesProviderMetadata()
    {
        var result = ProviderMetadataCatalog.SupportsAccountIdentity("github-copilot");

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseSharedSubDetailCollapsePreference_DelegatesToProviderMetadata()
    {
        var result = ProviderMetadataCatalog.ShouldUseSharedSubDetailCollapsePreference("codex.spark");

        Assert.False(result);
    }

    [Fact]
    public void ShouldRenderAsSettingsSubItem_DelegatesToProviderMetadata()
    {
        var result = ProviderMetadataCatalog.ShouldRenderAsSettingsSubItem("antigravity.some-model");

        Assert.False(result);
    }
}
