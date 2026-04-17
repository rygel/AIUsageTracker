// <copyright file="App.Screenshots.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using AIUsageTracker.Core.Models;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class App
{
    private const double ScreenshotScaleFactor = 2.0;
    private const double ScreenshotDpi = 96.0 * ScreenshotScaleFactor;

    public static void RenderWindowContent(Window window, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("Window content is not a FrameworkElement.");
        }

        var (width, height) = MeasureWindowContent(window, root);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display);
        root.SetValue(TextOptions.TextHintingModeProperty, TextHintingMode.Fixed);
        root.SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType);
        root.SetValue(RenderOptions.ClearTypeHintProperty, ClearTypeHint.Enabled);

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * ScreenshotScaleFactor));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * ScreenshotScaleFactor));
        var opaqueBitmap = ComposeOpaqueBitmap(window, root, width, height, pixelWidth, pixelHeight);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(opaqueBitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static (double Width, double Height) MeasureWindowContent(Window window, FrameworkElement root)
    {
        var width = window.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = root.Width;
        }

        if (double.IsNaN(width) || width <= 0)
        {
            width = Math.Max(1, root.ActualWidth);
        }

        if (width <= 0)
        {
            width = 380;
        }

        var height = window.Height;
        if (double.IsNaN(height) || height <= 0)
        {
            root.Measure(new Size(width, double.PositiveInfinity));
            height = Math.Max(1, root.DesiredSize.Height);
        }

        return (width, height);
    }

    private static FormatConvertedBitmap ComposeOpaqueBitmap(
        Window window,
        FrameworkElement root,
        double width,
        double height,
        int pixelWidth,
        int pixelHeight)
    {
        var backgroundBrush = CreateBackgroundBrush(window);
        var contentBitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, ScreenshotDpi, ScreenshotDpi, PixelFormats.Pbgra32);
        contentBitmap.Render(root);
        contentBitmap.Freeze();

        var composedVisual = new DrawingVisual();
        using (var dc = composedVisual.RenderOpen())
        {
            dc.DrawRectangle(brush: backgroundBrush, pen: null, rectangle: new Rect(0, 0, width, height));
            dc.DrawImage(contentBitmap, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, ScreenshotDpi, ScreenshotDpi, PixelFormats.Pbgra32);
        bitmap.Render(composedVisual);
        bitmap.Freeze();

        var opaqueBitmap = new FormatConvertedBitmap(source: bitmap, destinationFormat: PixelFormats.Bgr24, destinationPalette: null, alphaThreshold: 0);
        opaqueBitmap.Freeze();
        return opaqueBitmap;
    }

    private static SolidColorBrush CreateBackgroundBrush(Window window)
    {
        var backgroundBrush = window.Background is SolidColorBrush solidBackground
            ? new SolidColorBrush(Color.FromRgb(solidBackground.Color.R, solidBackground.Color.G, solidBackground.Color.B))
            : Brushes.Black;
        backgroundBrush.Freeze();
        return backgroundBrush;
    }

    private static string ResolveScreenshotsDirectory()
    {
        var currentDocs = Path.Combine(Environment.CurrentDirectory, "docs");
        if (Directory.Exists(currentDocs))
        {
            return currentDocs;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "docs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return currentDocs;
    }

    private static string? GetArgumentValue(IReadOnlyList<string> args, string argumentName)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private async Task RunHeadlessScreenshotCaptureAsync(string[] args)
    {
        var logger = CreateLogger<App>();
        try
        {
            var selectedTheme = AppTheme.Dark;
            var themeArg = GetArgumentValue(args, "--theme");
            if (!string.IsNullOrWhiteSpace(themeArg) && !Enum.TryParse<AppTheme>(themeArg, ignoreCase: true, out selectedTheme))
            {
                throw new ArgumentException($"Unknown theme '{themeArg}'.", nameof(args));
            }

            var isThemeSmokeMode = args.Contains("--theme-smoke", StringComparer.OrdinalIgnoreCase);
            var isCardCatalogMode = args.Contains("--card-catalog", StringComparer.OrdinalIgnoreCase);
            this.ConfigureHeadlessScreenshotPreferences(selectedTheme);
            var screenshotsDir = ResolveOutputDirectory(args);
            Directory.CreateDirectory(screenshotsDir);

            if (isThemeSmokeMode)
            {
                var smokeFileName = $"theme_smoke_{selectedTheme.ToString().ToLowerInvariant()}.png";
                await CaptureMainWindowScreenshotAsync(Path.Combine(screenshotsDir, smokeFileName)).ConfigureAwait(true);
                return;
            }

            if (isCardCatalogMode)
            {
                await this.CaptureCardCatalogAsync(screenshotsDir).ConfigureAwait(true);
                return;
            }

            await CaptureMainWindowScreenshotAsync(Path.Combine(screenshotsDir, "screenshot_dashboard_privacy.png")).ConfigureAwait(true);
            await CaptureSettingsScreenshotsAsync(screenshotsDir).ConfigureAwait(true);
            this.CaptureInfoScreenshot(Path.Combine(screenshotsDir, "screenshot_info_privacy.png"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Headless screenshot capture failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            this.Shutdown();
        }
    }

    private static string ResolveOutputDirectory(IReadOnlyList<string> args)
    {
        var outputDirectoryArg = GetArgumentValue(args, "--output-dir");
        return string.IsNullOrWhiteSpace(outputDirectoryArg)
            ? ResolveScreenshotsDirectory()
            : outputDirectoryArg;
    }

    private void ConfigureHeadlessScreenshotPreferences(AppTheme selectedTheme)
    {
        Preferences = new AppPreferences
        {
            AlwaysOnTop = true,
            ShowUsedPercentages = false,
            ColorThresholdYellow = 60,
            ColorThresholdRed = 80,
            FontFamily = "Segoe UI",
            FontSize = 12,
            FontBold = false,
            FontItalic = false,
            IsPrivacyMode = true,
            Theme = selectedTheme,
        };

        ApplyTheme(Preferences.Theme);
        SetPrivacyMode(true);
    }

    private static async Task CaptureMainWindowScreenshotAsync(string outputPath)
    {
        var window = Host.Services.GetRequiredService<MainWindow>();
        try
        {
            await window.PrepareForHeadlessScreenshotAsync(deterministic: true).ConfigureAwait(true);
            await WaitForDispatcherIdleAsync(window).ConfigureAwait(true);
            RenderWindowContent(window, outputPath);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task CaptureSettingsScreenshotsAsync(string outputDirectory)
    {
        var window = Host.Services.GetRequiredService<SettingsWindow>();
        try
        {
            await window.CaptureHeadlessTabScreenshotsAsync(outputDirectory).ConfigureAwait(true);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitForDispatcherIdleAsync(Window window)
    {
#pragma warning disable VSTHRD001 // WPF screenshot capture needs the window dispatcher to reach idle before rendering.
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.ConfigureAwait(true);
#pragma warning restore VSTHRD001
    }

    private async Task CaptureCardCatalogAsync(string outputDirectory)
    {
        var catalogDir = Path.Combine(outputDirectory, "card-catalog");
        Directory.CreateDirectory(catalogDir);
        var logger = CreateLogger<App>();
        var captured = new List<(string FileName, string Label, string Description)>();

        foreach (var permutation in CardCatalogPermutations.All)
        {
            var fileName = $"card_{permutation.Slug}.png";
            var outputPath = Path.Combine(catalogDir, fileName);
            logger.LogInformation("Capturing card catalog: {Slug}", permutation.Slug);

            var window = Host.Services.GetRequiredService<MainWindow>();
            try
            {
                await window.PrepareForHeadlessScreenshotAsync(deterministic: true).ConfigureAwait(true);

                // Apply the permutation AFTER the fixture loads — the fixture resets
                // preferences to defaults, so we must override afterwards.
                permutation.Apply(Preferences);
                window.ApplyPreferencesAndRerender();
                await WaitForDispatcherIdleAsync(window).ConfigureAwait(true);
                RenderWindowContent(window, outputPath);
                captured.Add((fileName, permutation.Label, permutation.Description));
            }
            finally
            {
                window.Close();
            }
        }

        // Reset to defaults after catalog capture.
        this.ConfigureHeadlessScreenshotPreferences(Preferences.Theme);

        // Generate markdown index.
        var markdown = CardCatalogPermutations.GenerateMarkdown(captured);
        var markdownPath = Path.Combine(catalogDir, "CARD-CATALOG.md");
        await File.WriteAllTextAsync(markdownPath, markdown).ConfigureAwait(true);
        logger.LogInformation("Card catalog: {Count} screenshots + markdown index written to {Dir}", captured.Count, catalogDir);
    }

    private void CaptureInfoScreenshot(string outputPath)
    {
        var window = this.InfoDialogFactory();
        try
        {
            if (window is InfoDialog infoDialog)
            {
                infoDialog.PrepareForHeadlessScreenshot();
            }

            window.UpdateLayout();
            RenderWindowContent(window, outputPath);
        }
        finally
        {
            window.Close();
        }
    }
}
