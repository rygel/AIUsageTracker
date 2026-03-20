using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

public class WpfScreenshotService : IScreenshotService, IWpfScreenshotService
{
    private const double ScreenshotScaleFactor = 2.0;
    private const double ScreenshotDpi = 96.0 * ScreenshotScaleFactor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WpfScreenshotService> _logger;
    private readonly IThemeService _themeService;

    public WpfScreenshotService(IServiceProvider serviceProvider, ILogger<WpfScreenshotService> logger, IThemeService themeService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _themeService = themeService;
    }

    public async Task RunHeadlessScreenshotCaptureAsync(string[] args)
    {
        try
        {
            var selectedTheme = AppTheme.Dark;
            var themeArg = GetArgumentValue(args, "--theme");
            if (!string.IsNullOrWhiteSpace(themeArg) && !Enum.TryParse<AppTheme>(themeArg, ignoreCase: true, out selectedTheme))
            {
                throw new ArgumentException($"Unknown theme '{themeArg}'.", nameof(args));
            }

            var isThemeSmokeMode = args.Contains("--theme-smoke", StringComparer.OrdinalIgnoreCase);

            // Set up preferences for screenshots
            App.Preferences = new AppPreferences
            {
                AlwaysOnTop = true,
                ShowUsedPercentages = true,
                ColorThresholdYellow = 60,
                ColorThresholdRed = 80,
                FontFamily = "Segoe UI",
                FontSize = 12,
                FontBold = false,
                FontItalic = false,
                IsPrivacyMode = true,
                Theme = selectedTheme
            };
            
            _themeService.ApplyTheme(selectedTheme);
            App.SetPrivacyMode(true);

            var outputDirectoryArg = GetArgumentValue(args, "--output-dir");
            
            // Resolve screenshots directory
            var screenshotsDir = string.IsNullOrWhiteSpace(outputDirectoryArg)
                ? ResolveScreenshotsDirectory()
                : outputDirectoryArg;
            Directory.CreateDirectory(screenshotsDir);

            if (isThemeSmokeMode)
            {
                var smokeFileName = $"theme_smoke_{selectedTheme.ToString().ToLowerInvariant()}.png";
                await CaptureMainWindowScreenshotAsync(Path.Combine(screenshotsDir, smokeFileName));
                return;
            }

            await CaptureMainWindowScreenshotAsync(Path.Combine(screenshotsDir, "screenshot_dashboard_privacy.png"));
            await CaptureSettingsScreenshotsAsync(screenshotsDir);
            CaptureInfoScreenshot(Path.Combine(screenshotsDir, "screenshot_info_privacy.png"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Headless screenshot capture failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            Application.Current.Shutdown();
        }
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

    public void RenderWindowContent(Window window, string outputPath)
    {
        if (window.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("Window content is not a FrameworkElement.");
        }

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

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display);
        root.SetValue(TextOptions.TextHintingModeProperty, TextHintingMode.Fixed);
        root.SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType);
        root.SetValue(RenderOptions.ClearTypeHintProperty, ClearTypeHint.Enabled);

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * ScreenshotScaleFactor));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * ScreenshotScaleFactor));
        var backgroundBrush = window.Background is SolidColorBrush solidBackground
            ? new SolidColorBrush(Color.FromRgb(solidBackground.Color.R, solidBackground.Color.G, solidBackground.Color.B))
            : Brushes.Black;
        backgroundBrush.Freeze();

        var contentBitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, ScreenshotDpi, ScreenshotDpi, PixelFormats.Pbgra32);
        contentBitmap.Render(root);
        contentBitmap.Freeze();

        var composedVisual = new DrawingVisual();
        using (var dc = composedVisual.RenderOpen())
        {
            dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, width, height));
            dc.DrawImage(contentBitmap, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, ScreenshotDpi, ScreenshotDpi, PixelFormats.Pbgra32);
        bitmap.Render(composedVisual);
        bitmap.Freeze();

        var opaqueBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        opaqueBitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(opaqueBitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private async Task CaptureMainWindowScreenshotAsync(string outputPath)
    {
        var window = _serviceProvider.GetRequiredService<MainWindow>();
        try
        {
            await window.PrepareForHeadlessScreenshotAsync(deterministic: true);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            RenderWindowContent(window, outputPath);
        }
        finally
        {
            window.Close();
        }
    }

    private async Task CaptureSettingsScreenshotsAsync(string outputDirectory)
    {
        var window = _serviceProvider.GetRequiredService<SettingsWindow>();
        try
        {
            await window.CaptureHeadlessTabScreenshotsAsync(outputDirectory);
        }
        finally
        {
            window.Close();
        }
    }

    private void CaptureInfoScreenshot(string outputPath)
    {
        var app = (App)Application.Current;
        var window = app.InfoDialogFactory();
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
}
