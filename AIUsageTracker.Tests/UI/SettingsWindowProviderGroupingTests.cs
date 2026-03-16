// <copyright file="SettingsWindowProviderGroupingTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class SettingsWindowProviderGroupingTests
{
    [Fact]
    public void ShouldRenderAsSettingsSubItem_ReturnsFalse_ForCodexSparkDerived()
    {
        var result = SettingsWindow.ShouldRenderAsSettingsSubItem("codex.spark", isDerived: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRenderAsSettingsSubItem_ReturnsFalse_ForAntigravityDerivedChild()
    {
        var result = SettingsWindow.ShouldRenderAsSettingsSubItem("antigravity.some-model", isDerived: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRenderAsSettingsSubItem_UsesProviderMetadata()
    {
        var result = SettingsWindow.ShouldRenderAsSettingsSubItem(
            "codex.spark",
            isDerived: true);

        Assert.False(result);
    }
}
