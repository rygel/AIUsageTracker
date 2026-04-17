// <copyright file="ThemeCompletenessTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Verifies that every theme defines all required resource keys.
/// Catches regressions where a theme is added or modified but
/// missing color definitions, causing invisible UI elements.
/// </summary>
public class ThemeCompletenessTests
{
    private static readonly string[] RequiredColorKeys =
    {
        "Background",
        "HeaderBackground",
        "FooterBackground",
        "BorderColor",
        "PrimaryText",
        "SecondaryText",
        "TertiaryText",
        "AccentColor",
        "AccentForeground",
        "ButtonBackground",
        "ButtonHover",
        "ButtonPressed",
        "ControlBackground",
        "ControlBorder",
        "InputBackground",
        "TabUnselected",
        "ComboBoxBackground",
        "ComboBoxItemHover",
        "ProgressBarBackground",
        "StatusTextWarning",
        "GroupHeaderBackground",
        "GroupHeaderBorder",
        "CardBackground",
        "CardBorder",
        "ScrollBarBackground",
        "ScrollBarForeground",
        "ScrollBarHover",
        "LinkForeground",
    };

    public static TheoryData<AppTheme> AllThemes =>
        new TheoryData<AppTheme>(Enum.GetValues<AppTheme>());

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void Theme_IsRegisteredInPalette(AppTheme theme)
    {
        // Verify that every AppTheme enum value has a corresponding palette entry.
        // App.ThemePalettes is accessed via ApplyTheme which validates internally.
        // If a theme is missing, ApplyTheme falls back to Dark — this test catches that.
        var hasPalette = App.HasThemePalette(theme);
        Assert.True(hasPalette, $"Theme '{theme}' has no palette defined in App.Themes.cs. Add it to ThemePalettes.");
    }

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void Theme_DefinesAllRequiredKeys(AppTheme theme)
    {
        var missingKeys = App.GetMissingThemeKeys(theme, RequiredColorKeys);
        Assert.Empty(missingKeys);
    }
}
