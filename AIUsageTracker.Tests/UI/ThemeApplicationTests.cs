// <copyright file="ThemeApplicationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// End-to-end tests that verify all windows and dialogs respect the active theme.
/// Catches regressions where a new window is created without theme-aware colors
/// (e.g., white background on dark theme).
/// </summary>
public class ThemeApplicationTests
{
    private static readonly TimeSpan StaTestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Scans all Window subclasses in UI.Slim and verifies they either:
    /// 1. Use DynamicResource bindings for Background/Foreground in XAML, OR
    /// 2. Set Background/Foreground from theme resources in code-behind
    ///
    /// This is a static analysis test — it checks the XAML for correct bindings
    /// without needing to instantiate windows (which require full DI).
    /// </summary>
    [Fact]
    public void AllWindows_UseThemeAwareBackgroundInXaml()
    {
        var assembly = typeof(App).Assembly;
        var windowTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(Window).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(windowTypes);

        var violations = new List<string>();

        foreach (var windowType in windowTypes)
        {
            // Check for XAML resource file
            var resourceName = windowType.FullName + ".xaml";
            var bamlName = windowType.Name.ToLowerInvariant() + ".baml";

            // Read the XAML source file directly
            var xamlPath = FindXamlFile(windowType);
            if (xamlPath == null)
            {
                continue; // No XAML file (code-only window like prompt dialogs)
            }

            var xamlContent = File.ReadAllText(xamlPath);

            // Check that Background uses DynamicResource, not a hardcoded color
            if (!xamlContent.Contains("Background=\"{DynamicResource"))
            {
                // Check if it's set to Transparent (acceptable) or inherits
                if (xamlContent.Contains("Background=\"Transparent\"") ||
                    !xamlContent.Contains("Background="))
                {
                    continue; // Transparent or inherited is OK
                }

                violations.Add($"{windowType.Name}: Background is not a DynamicResource binding");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Windows with non-themed backgrounds:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies that no Window subclass in UI.Slim creates child windows using
    /// 'new Window()' without applying theme resources. Searches for the pattern
    /// in source code.
    /// </summary>
    [Fact]
    public void NoUnthemedWindowCreation_InSourceCode()
    {
        var uiSlimDir = FindProjectDirectory("AIUsageTracker.UI.Slim");
        if (uiSlimDir == null)
        {
            return; // Can't find source — skip in CI where source may not be at expected path
        }

        var violations = new List<string>();
        var csFiles = Directory.GetFiles(uiSlimDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj") && !f.Contains("bin"));

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            // Find "new Window" or "new Window()" that aren't in comments
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//") || line.StartsWith("*"))
                {
                    continue;
                }

                if (line.Contains("new Window") && !line.Contains("WindowInteropHelper"))
                {
                    // Check if Background is set from resources within ~10 lines
                    var contextEnd = Math.Min(i + 15, lines.Length);
                    var context = string.Join("\n", lines[i..contextEnd]);

                    if (!context.Contains("Background") ||
                        (!context.Contains("DynamicResource") &&
                         !context.Contains("FindResource") &&
                         !context.Contains("Resources[") &&
                         !context.Contains("res[")))
                    {
                        violations.Add($"{fileName}:{i + 1}: creates Window without theme Background");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Unthemed window creation found:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies that applying a dark theme sets the Background resource to a dark color,
    /// and applying a light theme sets it to a light color.
    /// </summary>
    [Fact]
    public void ApplyTheme_DarkTheme_ProducesDarkBackground()
    {
        var darkThemes = new[] { AppTheme.Dark, AppTheme.Dracula, AppTheme.Nord, AppTheme.Monokai, AppTheme.OneDark };

        foreach (var theme in darkThemes)
        {
            var palette = GetPalette(theme);
            if (palette == null)
            {
                continue;
            }

            Assert.True(
                palette.TryGetValue("Background", out var bg),
                $"Theme {theme} missing Background key");

            var luminance = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
            Assert.True(
                luminance < 100,
                $"Theme {theme} Background ({bg}) is too bright for a dark theme (luminance={luminance:F0})");
        }
    }

    [Fact]
    public void ApplyTheme_LightTheme_ProducesLightBackground()
    {
        var lightThemes = new[] { AppTheme.Light, AppTheme.SolarizedLight, AppTheme.CatppuccinLatte };

        foreach (var theme in lightThemes)
        {
            var palette = GetPalette(theme);
            if (palette == null)
            {
                continue;
            }

            Assert.True(
                palette.TryGetValue("Background", out var bg),
                $"Theme {theme} missing Background key");

            var luminance = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
            Assert.True(
                luminance > 150,
                $"Theme {theme} Background ({bg}) is too dark for a light theme (luminance={luminance:F0})");
        }
    }

    [Fact]
    public void AllThemes_TextIsReadableAgainstBackground()
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            var palette = GetPalette(theme);
            if (palette == null)
            {
                continue;
            }

            if (!palette.TryGetValue("Background", out var bg) ||
                !palette.TryGetValue("PrimaryText", out var text))
            {
                continue;
            }

            var contrast = GetContrastRatio(bg, text);
            Assert.True(
                contrast >= 3.0,
                $"Theme {theme}: PrimaryText contrast against Background is {contrast:F1} (minimum 3.0 for readability)");
        }
    }

    private static Dictionary<string, Color>? GetPalette(AppTheme theme)
    {
        // Access the private ThemePalettes dictionary via reflection
        var field = typeof(App).GetField("ThemePalettes", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetValue(null) is not Dictionary<AppTheme, Dictionary<string, Color>> palettes)
        {
            return null;
        }

        return palettes.TryGetValue(theme, out var palette) ? palette : null;
    }

    private static double GetContrastRatio(Color c1, Color c2)
    {
        var l1 = GetRelativeLuminance(c1);
        var l2 = GetRelativeLuminance(c2);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double GetRelativeLuminance(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static string? FindXamlFile(Type windowType)
    {
        var dir = FindProjectDirectory("AIUsageTracker.UI.Slim");
        if (dir == null)
        {
            return null;
        }

        var name = windowType.Name + ".xaml";
        var files = Directory.GetFiles(dir, name, SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj") && !f.Contains("bin"))
            .ToList();
        return files.FirstOrDefault();
    }

    private static string? FindProjectDirectory(string projectName)
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var candidate = Path.Combine(dir, projectName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
