// <copyright file="CollapsibleHeaderPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class CollapsibleHeaderPresentationCatalogTests
{
    [Fact]
    public void Create_GroupHeader_UsesGroupStyles()
    {
        var presentation = CollapsibleHeaderPresentationCatalog.Create("Plans & Quotas", isGroupHeader: true);

        Assert.Equal(new Thickness(0, 8, 0, 4), presentation.Margin);
        Assert.Equal(10.0, presentation.ToggleFontSize);
        Assert.Equal(FontWeights.Bold, presentation.TitleFontWeight);
        Assert.Equal(1.0, presentation.ToggleOpacity);
        Assert.Equal(0.5, presentation.LineOpacity);
        Assert.Equal("PLANS & QUOTAS", presentation.TitleText);
        Assert.True(presentation.UseAccentForTitle);
    }

    [Fact]
    public void Create_SubHeader_UsesSubsectionStyles()
    {
        var presentation = CollapsibleHeaderPresentationCatalog.Create("GitHub Copilot Details", isGroupHeader: false);

        Assert.Equal(new Thickness(20, 4, 0, 2), presentation.Margin);
        Assert.Equal(9.0, presentation.ToggleFontSize);
        Assert.Equal(FontWeights.Normal, presentation.TitleFontWeight);
        Assert.Equal(0.8, presentation.ToggleOpacity);
        Assert.Equal(0.3, presentation.LineOpacity);
        Assert.Equal("GitHub Copilot Details", presentation.TitleText);
        Assert.False(presentation.UseAccentForTitle);
    }
}
