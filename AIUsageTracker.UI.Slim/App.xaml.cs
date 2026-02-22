using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.AgentClient;
using System.Runtime.InteropServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsageTracker.UI.Slim;

public partial class App : Application
{
    public static AgentService AgentService { get; } = new();
    public static AppPreferences Preferences { get; set; } = new();
    public static bool IsPrivacyMode { get; set; } = false;
    private const double ScreenshotScaleFactor = 2.0;
    private const double ScreenshotDpi = 96.0 * ScreenshotScaleFactor;
    private TaskbarIcon? _trayIcon;
    private readonly Dictionary<string, TaskbarIcon> _providerTrayIcons = new();
    private MainWindow? _mainWindow;

    public static event EventHandler<bool>? PrivacyChanged;

    public static void ApplyTheme(AppTheme theme)
    {
        Preferences.Theme = theme;
        var resources = Current?.Resources;
        if (resources == null)
        {
            return;
        }

        if (theme == AppTheme.Light)
        {
            SetBrushColor(resources, "Background", Color.FromRgb(247, 247, 247));
            SetBrushColor(resources, "HeaderBackground", Color.FromRgb(237, 237, 237));
            SetBrushColor(resources, "FooterBackground", Color.FromRgb(237, 237, 237));
            SetBrushColor(resources, "BorderColor", Color.FromRgb(208, 208, 208));
            SetBrushColor(resources, "PrimaryText", Color.FromRgb(32, 32, 32));
            SetBrushColor(resources, "SecondaryText", Color.FromRgb(85, 85, 85));
            SetBrushColor(resources, "TertiaryText", Color.FromRgb(120, 120, 120));
            SetBrushColor(resources, "AccentColor", Color.FromRgb(0, 120, 212));
            SetBrushColor(resources, "AccentForeground", Color.FromRgb(255, 255, 255));

            SetBrushColor(resources, "ButtonBackground", Color.FromRgb(230, 230, 230));
            SetBrushColor(resources, "ButtonHover", Color.FromRgb(218, 218, 218));
            SetBrushColor(resources, "ButtonPressed", Color.FromRgb(0, 120, 212));
            SetBrushColor(resources, "ControlBackground", Color.FromRgb(255, 255, 255));
            SetBrushColor(resources, "ControlBorder", Color.FromRgb(196, 196, 196));
            SetBrushColor(resources, "InputBackground", Color.FromRgb(255, 255, 255));
            SetBrushColor(resources, "TabUnselected", Color.FromRgb(236, 236, 236));
            SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(255, 255, 255));
            SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(232, 240, 254));

            SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(230, 230, 230));
            SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(138, 109, 59));
            SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(240, 240, 240));
            SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(210, 210, 210));
            SetBrushColor(resources, "CardBackground", Color.FromRgb(255, 255, 255));
            SetBrushColor(resources, "CardBorder", Color.FromRgb(215, 215, 215));
            SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(245, 245, 245));
            SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(170, 170, 170));
            SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(140, 140, 140));
            SetBrushColor(resources, "LinkForeground", Color.FromRgb(0, 95, 184));
        }
        else
        {
            SetBrushColor(resources, "Background", Color.FromRgb(30, 30, 30));
            SetBrushColor(resources, "HeaderBackground", Color.FromRgb(37, 37, 38));
            SetBrushColor(resources, "FooterBackground", Color.FromRgb(37, 37, 38));
            SetBrushColor(resources, "BorderColor", Color.FromRgb(51, 51, 51));
            SetBrushColor(resources, "PrimaryText", Color.FromRgb(255, 255, 255));
            SetBrushColor(resources, "SecondaryText", Color.FromRgb(170, 170, 170));
            SetBrushColor(resources, "TertiaryText", Color.FromRgb(136, 136, 136));
            SetBrushColor(resources, "AccentColor", Color.FromRgb(0, 122, 204));
            SetBrushColor(resources, "AccentForeground", Color.FromRgb(255, 255, 255));

            SetBrushColor(resources, "ButtonBackground", Color.FromRgb(68, 68, 68));
            SetBrushColor(resources, "ButtonHover", Color.FromRgb(85, 85, 85));
            SetBrushColor(resources, "ButtonPressed", Color.FromRgb(0, 122, 204));
            SetBrushColor(resources, "ControlBackground", Color.FromRgb(45, 45, 48));
            SetBrushColor(resources, "ControlBorder", Color.FromRgb(67, 67, 70));
            SetBrushColor(resources, "InputBackground", Color.FromRgb(45, 45, 48));
            SetBrushColor(resources, "TabUnselected", Color.FromRgb(37, 37, 38));
            SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(45, 45, 48));
            SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(62, 62, 66));

            SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(45, 45, 48));
            SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(184, 134, 11));
            SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(37, 37, 38));
            SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(51, 51, 51));
            SetBrushColor(resources, "CardBackground", Color.FromRgb(40, 40, 40));
            SetBrushColor(resources, "CardBorder", Color.FromRgb(55, 55, 55));
            SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(30, 30, 30));
            SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(78, 78, 78));
            SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(110, 110, 110));
            SetBrushColor(resources, "LinkForeground", Color.FromRgb(55, 148, 255));
        }
    }

    private static void SetBrushColor(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    public static void SetPrivacyMode(bool enabled)
    {
        IsPrivacyMode = enabled;
        Preferences.IsPrivacyMode = enabled;
        PrivacyChanged?.Invoke(null, enabled);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--debug"))
        {
            AllocConsole();
            Console.WriteLine("");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  AIUsageTracker.UI - DEBUG MODE");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine($"  Started:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Process ID: {Environment.ProcessId}");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("");
            
            AgentService.LogDiagnostic("AI Usage Tracker UI Debug Mode Enabled");
        }

        base.OnStartup(e);

        if (e.Args.Contains("--test", StringComparer.OrdinalIgnoreCase) &&
            e.Args.Contains("--screenshot", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunHeadlessScreenshotCaptureAsync();
            return;
        }
        
        // Load UI preferences from local Slim storage
        LoadPreferencesAsync().GetAwaiter().GetResult();
        
        // Create tray icon
        InitializeTrayIcon();
        
        // Create and show main window
        _mainWindow = new MainWindow();
        _mainWindow.Show();
    }

    private async Task RunHeadlessScreenshotCaptureAsync()
    {
        try
        {
            Preferences = new AppPreferences
            {
                AlwaysOnTop = true,
                InvertProgressBar = true,
                InvertCalculations = false,
                ColorThresholdYellow = 60,
                ColorThresholdRed = 80,
                FontFamily = "Segoe UI",
                FontSize = 12,
                FontBold = false,
                FontItalic = false,
                IsPrivacyMode = true,
                Theme = AppTheme.Dark
            };
            ApplyTheme(Preferences.Theme);
            SetPrivacyMode(true);

            var screenshotsDir = ResolveScreenshotsDirectory();
            Directory.CreateDirectory(screenshotsDir);

            await CaptureMainWindowScreenshotAsync(Path.Combine(screenshotsDir, "screenshot_dashboard_privacy.png"));
            await CaptureSettingsScreenshotsAsync(screenshotsDir);
            CaptureInfoScreenshot(Path.Combine(screenshotsDir, "screenshot_info_privacy.png"));
        }
        catch (Exception ex)
        {
            AgentService.LogDiagnostic($"Headless screenshot capture failed: {ex}");
            Environment.ExitCode = 1;
        }
        finally
        {
            Shutdown();
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

    internal static void RenderWindowContent(Window window, string outputPath)
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
        var window = new MainWindow();
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
        var window = new SettingsWindow();
        try
        {
            await window.CaptureHeadlessTabScreenshotsAsync(outputDirectory);
        }
        finally
        {
            window.Close();
        }
    }

    private static void CaptureInfoScreenshot(string outputPath)
    {
        var window = new InfoDialog();
        try
        {
            window.PrepareForHeadlessScreenshot();
            window.UpdateLayout();
            RenderWindowContent(window, outputPath);
        }
        finally
        {
            window.Close();
        }
    }

    private void InitializeTrayIcon()
    {
        // Create context menu
        var contextMenu = new ContextMenu();
        
        // Show menu item
        var showMenuItem = new MenuItem { Header = "Show" };
        showMenuItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(showMenuItem);
        
        // Separator
        contextMenu.Items.Add(new Separator());
        
        // Info menu item
        var infoMenuItem = new MenuItem { Header = "Info" };
        infoMenuItem.Click += (s, e) =>
        {
            var infoDialog = new InfoDialog();
            // If main window is visible, center over it, otherwise center screen (default)
            if (_mainWindow != null && _mainWindow.IsVisible)
            {
                infoDialog.Owner = _mainWindow;
                infoDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            infoDialog.Show();
            infoDialog.Activate();
        };
        contextMenu.Items.Add(infoMenuItem);
        
        // Separator
        contextMenu.Items.Add(new Separator());
        
        // Exit menu item
        var exitMenuItem = new MenuItem { Header = "Exit" };
        exitMenuItem.Click += (s, e) =>
        {
            Shutdown();
        };
        contextMenu.Items.Add(exitMenuItem);
        
        // Create tray icon
        _trayIcon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon("Assets/app_icon.ico"),
            ToolTipText = "AI Usage Tracker",
            ContextMenu = contextMenu,
            DoubleClickCommand = new RelayCommand(() =>
            {
                ShowMainWindow();
            })
        };
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            return;
        }

        _mainWindow.ShowAndActivate();
    }

    public void UpdateProviderTrayIcons(List<ProviderUsage> usages, List<ProviderConfig> configs, AppPreferences? prefs = null)
    {
        var desiredIcons = new Dictionary<string, (string ToolTip, double Percentage, bool IsQuota)>(StringComparer.OrdinalIgnoreCase);
        var yellowThreshold = prefs?.ColorThresholdYellow ?? 60;
        var redThreshold = prefs?.ColorThresholdRed ?? 80;
        var invert = prefs?.InvertProgressBar ?? false;

        foreach (var config in configs)
        {
            var usage = usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (usage == null)
            {
                continue;
            }

            if (config.ShowInTray &&
                usage.IsAvailable &&
                !usage.Description.Contains("unknown", StringComparison.OrdinalIgnoreCase))
            {
                var isQuota = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
                desiredIcons[config.ProviderId] = ($"{usage.ProviderName}: {usage.Description}", usage.RequestsPercentage, isQuota);
            }

            if (config.EnabledSubTrays == null || usage.Details == null)
            {
                continue;
            }

            foreach (var subName in config.EnabledSubTrays)
            {
                var detail = usage.Details.FirstOrDefault(d => d.Name.Equals(subName, StringComparison.OrdinalIgnoreCase));
                if (detail == null)
                {
                    continue;
                }

                if (!IsSubTrayEligibleDetail(detail))
                {
                    continue;
                }

                var detailPercent = ParsePercent(detail.Used);
                if (!detailPercent.HasValue)
                {
                    continue;
                }

                var key = $"{config.ProviderId}:{subName}";
                var isQuotaSub = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
                desiredIcons[key] = (
                    $"{usage.ProviderName} - {subName}: {detail.Description} ({detail.Used})",
                    detailPercent.Value,
                    isQuotaSub
                );
            }
        }

        var currentKeys = _providerTrayIcons.Keys.ToList();
        foreach (var key in currentKeys)
        {
            if (desiredIcons.ContainsKey(key))
            {
                continue;
            }

            _providerTrayIcons[key].Dispose();
            _providerTrayIcons.Remove(key);
        }

        foreach (var kvp in desiredIcons)
        {
            var key = kvp.Key;
            var info = kvp.Value;
            var iconSource = GenerateUsageIcon(info.Percentage, yellowThreshold, redThreshold, invert, info.IsQuota);

            if (!_providerTrayIcons.ContainsKey(key))
            {
                var tray = new TaskbarIcon
                {
                    ToolTipText = info.ToolTip,
                    IconSource = iconSource
                };
                tray.TrayLeftMouseDown += (s, e) => ShowMainWindow();
                tray.TrayMouseDoubleClick += (s, e) => ShowMainWindow();
                _providerTrayIcons.Add(key, tray);
            }
            else
            {
                var tray = _providerTrayIcons[key];
                tray.ToolTipText = info.ToolTip;
                tray.IconSource = iconSource;
            }
        }
    }

    private static double? ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsedValue = value.Replace("%", string.Empty).Trim();
        return double.TryParse(parsedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, Math.Min(100, parsed))
            : null;
    }

    private static bool IsSubTrayEligibleDetail(ProviderUsageDetail detail)
    {
        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            return false;
        }

        return !detail.Name.Contains("window", StringComparison.OrdinalIgnoreCase) &&
               !detail.Name.Contains("credit", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource GenerateUsageIcon(double percentage, int yellowThreshold, int redThreshold, bool invert = false, bool isQuota = false)
    {
        var size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 20)), null, new Rect(0, 0, size, size));
            dc.DrawRectangle(null, new Pen(Brushes.DimGray, 1), new Rect(0.5, 0.5, size - 1, size - 1));

            var fillBrush = isQuota
                ? (percentage < (100 - redThreshold)
                    ? Brushes.Crimson
                    : (percentage < (100 - yellowThreshold) ? Brushes.Gold : Brushes.MediumSeaGreen))
                : (percentage > redThreshold
                    ? Brushes.Crimson
                    : (percentage > yellowThreshold ? Brushes.Gold : Brushes.MediumSeaGreen));

            var barWidth = size - 6;
            var barHeight = size - 6;
            double fillHeight;
            if (invert)
            {
                var remaining = Math.Max(0, 100.0 - percentage);
                fillHeight = (remaining / 100.0) * barHeight;
            }
            else
            {
                fillHeight = (percentage / 100.0) * barHeight;
            }

            dc.DrawRectangle(fillBrush, null, new Rect(3, size - 3 - fillHeight, barWidth, fillHeight));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        foreach (var tray in _providerTrayIcons.Values)
        {
            tray.Dispose();
        }
        _providerTrayIcons.Clear();
        base.OnExit(e);
    }

    private static async Task LoadPreferencesAsync()
    {
        try
        {
            Preferences = await UiPreferencesStore.LoadAsync();
            IsPrivacyMode = Preferences.IsPrivacyMode;
            ApplyTheme(Preferences.Theme);
        }
        catch
        {
            // Use defaults
            ApplyTheme(AppTheme.Dark);
        }
    }
}

// Simple relay command implementation
public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { System.Windows.Input.CommandManager.RequerySuggested += value; }
        remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}

public partial class App
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AllocConsole();
}

