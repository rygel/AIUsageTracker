using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Infrastructure.Configuration;
using AIConsumptionTracker.Infrastructure.Providers;
using AIConsumptionTracker.Infrastructure.Services;
using AIConsumptionTracker.Infrastructure.Helpers;
using AIConsumptionTracker.UI.Services;
using System.Runtime.InteropServices;

// =============================================================================
// ⚠️  AI ASSISTANTS: COLOR LOGIC WARNING - SEE LINE ~426
// =============================================================================
// The tray icon color logic in GenerateUsageIcon() follows strict rules
// documented in DESIGN.md. DO NOT modify without developer approval.
// Must match MainWindow.GetProgressBarColor() behavior exactly.
// =============================================================================

namespace AIConsumptionTracker.UI
{
    public partial class App : Application
    {
        private TaskbarIcon? _taskbarIcon;
        private readonly Dictionary<string, TaskbarIcon> _providerTrayIcons = new();
        private IHost? _host;
        public IServiceProvider Services => _host!.Services;

        public event EventHandler<bool>? PrivacyChanged;

        public async Task TogglePrivacyMode(bool? forcedState = null)
        {
            var configLoader = Services.GetRequiredService<IConfigLoader>();
            var prefs = await configLoader.LoadPreferencesAsync();
            
            prefs.IsPrivacyMode = forcedState ?? !prefs.IsPrivacyMode;
            await configLoader.SavePreferencesAsync(prefs);

            PrivacyChanged?.Invoke(this, prefs.IsPrivacyMode);
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);

                    var logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AIConsumptionTracker",
                        "logs",
                        $"app_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    logging.AddProvider(new FileLoggerProvider(logPath));
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddHttpClient();
                    services.AddSingleton<IConfigLoader, JsonConfigLoader>();
                    services.AddSingleton<IFontProvider, WpfFontProvider>();

                    services.AddTransient<IProviderService, OpenCodeProvider>();
                    services.AddTransient<IProviderService, ZaiProvider>();
                    services.AddTransient<IProviderService, OpenRouterProvider>();
                    services.AddTransient<IProviderService, AntigravityProvider>();
                    services.AddTransient<IProviderService, GeminiProvider>();
                    services.AddTransient<IProviderService, KimiProvider>();
                    services.AddTransient<IProviderService, DeepSeekProvider>();
                    services.AddTransient<IProviderService, OpenAIProvider>();
                    services.AddTransient<IProviderService, ClaudeCodeProvider>();
                    services.AddTransient<IProviderService, MistralProvider>();
                    services.AddTransient<IProviderService, GenericPayAsYouGoProvider>();
                    services.AddTransient<IProviderService, GitHubCopilotProvider>();
                    services.AddTransient<IProviderService, CodexProvider>();
                    services.AddTransient<IProviderService, MinimaxProvider>();
                    services.AddTransient<IProviderService, XiaomiProvider>();
                    services.AddTransient<IUpdateCheckerService, GitHubUpdateChecker>();

                    services.AddSingleton<IGitHubAuthService, GitHubAuthService>();
                    services.AddSingleton<INotificationService, WindowsNotificationService>();

                    services.AddSingleton<ProviderManager>();
                    services.AddTransient<MainWindow>();
                    services.AddTransient<SettingsWindow>();
                    services.AddTransient<InfoDialog>();
                })
                .Build();

            await _host.StartAsync();

            var notificationService = _host.Services.GetRequiredService<INotificationService>();
            notificationService.Initialize();
            notificationService.OnNotificationClicked += OnNotificationClicked;

            bool isTestMode = e.Args.Any(arg => arg == "--test");
            bool isScreenshotMode = e.Args.Any(arg => arg == "--screenshot");

            if (isScreenshotMode && isTestMode)
            {
                await HandleScreenshotMode();
                return;
            }

            var configLoader = Services.GetRequiredService<IConfigLoader>();
            var prefs = await configLoader.LoadPreferencesAsync();

            if (prefs.StartWithWindows)
            {
                await SetStartupTaskAsync();
            }

            var providerManager = _host.Services.GetRequiredService<ProviderManager>();
            _ = Task.Run(() => providerManager.GetAllUsageAsync(forceRefresh: true));

            InitializeTrayIcon();
            await ShowDashboard();
        }

        private async Task SetStartupTaskAsync()
        {
            try
            {
                var appName = "AIConsumptionTracker";
                var exePath = Environment.ProcessPath;
                var startupPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    "Microsoft",
                    "Windows",
                    "CurrentVersion",
                    "Run");
                
                var command = $"\"{exePath}\"";
                
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    true))
                {
                    key.SetValue(appName, command, RegistryValueKind.String);
                    key.Close();
                }
            }
            catch (Exception ex)
            {
                // Registry operations can fail due to permissions or other issues
                // Fail silently since this is optional functionality
                var logger = Services.GetService<ILogger<App>>();
                logger?.LogWarning(ex, "Failed to set Windows startup: {Message}", ex.Message);
            }
        }

        private async Task HandleScreenshotMode()
        {
            var loader = Services.GetRequiredService<IConfigLoader>();
            var providerManager = Services.GetRequiredService<ProviderManager>();
            
            // Force Privacy Mode
            var prefs = await loader.LoadPreferencesAsync();
            prefs.IsPrivacyMode = true;

            // Wait for data
            var usages = await providerManager.GetAllUsageAsync(forceRefresh: true);

            var docsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../docs");
            if (!Directory.Exists(docsPath)) docsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");
            Directory.CreateDirectory(docsPath);

            // 1. Dashboard (Headless)
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Width = 350;
            mainWindow.Height = 650;
            await mainWindow.PrepareForScreenshot(prefs, usages);
            AIConsumptionTracker.UI.Services.ScreenshotService.SaveScreenshot(mainWindow, Path.Combine(docsPath, "screenshot_dashboard_privacy.png"));

            // 2. Settings (Headless)
            var settingsWindow = Services.GetRequiredService<SettingsWindow>();
            settingsWindow.Width = 650;
            settingsWindow.Height = 600;
            await settingsWindow.PrepareForScreenshot(prefs);
            await Task.Delay(200); // Give layout extra time
            AIConsumptionTracker.UI.Services.ScreenshotService.SaveScreenshot(settingsWindow, Path.Combine(docsPath, "screenshot_settings_privacy.png"));

            // 3. Info Dialog (Headless)
            var infoDialog = Services.GetRequiredService<InfoDialog>();
            infoDialog.Width = 400;
            infoDialog.Height = 350; // Increased height slightly
            await infoDialog.PrepareForScreenshot(prefs);
            AIConsumptionTracker.UI.Services.ScreenshotService.SaveScreenshot(infoDialog, Path.Combine(docsPath, "screenshot_info_privacy.png"));

            // 4. Context Menu (Headless)
            InitializeTrayIcon(); 
            if (_taskbarIcon?.ContextMenu != null)
            {
                var menu = _taskbarIcon.ContextMenu;
                AIConsumptionTracker.UI.Services.ScreenshotService.SaveScreenshot(menu, Path.Combine(docsPath, "screenshot_context_menu_privacy.png"));
            }

            // 5. Tray Icons
            SaveTrayIcon(0, 60, 80, Path.Combine(docsPath, "tray_icon_good.png"));
            SaveTrayIcon(75, 60, 80, Path.Combine(docsPath, "tray_icon_warning.png"));
            SaveTrayIcon(95, 60, 80, Path.Combine(docsPath, "tray_icon_danger.png"));

            Shutdown();
        }
    

        private void SaveTrayIcon(double percentage, int yellow, int red, string path)
        {
            var iconSource = GenerateUsageIcon(percentage, yellow, red, false);
            if (iconSource is RenderTargetBitmap rtb)
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.OpenWrite(path))
                {
                    encoder.Save(fs);
                }
            }
        }

        private void InitializeTrayIcon()
        {
            _taskbarIcon = new TaskbarIcon();
            _taskbarIcon.ToolTipText = "AI Consumption Tracker";
            _taskbarIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app_icon.png"));

            // Context Menu
            var contextMenu = new ContextMenu();
            
            var openItem = new MenuItem { Header = "Open" };
            openItem.Click += async (s, e) => await ShowDashboard();
            
            var settingsItem = new MenuItem { Header = "Settings" };
            settingsItem.Click += (s, e) => ShowSettings();
            
            var infoItem = new MenuItem { Header = "Info" };
            infoItem.Click += (s, e) => ShowInfo();
            
            var exitItem = new MenuItem { Header = "Exit" };
            exitItem.Click += (s, e) => ExitApp();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(infoItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = contextMenu;

            // Wire up single click and double click to show dashboard
            _taskbarIcon.TrayLeftMouseDown += async (s, e) => await ShowDashboard();
            _taskbarIcon.TrayMouseDoubleClick += async (s, e) => await ShowDashboard();
        }

        public void ShowSettings()
        {
            // Ensure dashboard is created
            if (_mainWindow == null)
            {
                _ = ShowDashboard();
            }
            
            if (_mainWindow == null) return;

            if (_mainWindow.Visibility != Visibility.Visible)
            {
                _mainWindow.Show();
            }
            _mainWindow.Activate();

            var settingsWindow = Services.GetRequiredService<SettingsWindow>();
            settingsWindow.Owner = _mainWindow;
            settingsWindow.Closed += async (s, e) => 
            {
                 // Refresh only if settings actually changed
                 if (settingsWindow.SettingsChanged && _mainWindow != null && _mainWindow.IsVisible && _mainWindow is MainWindow main)
                 {
                     await main.RefreshData(forceRefresh: true);
                 }
            };
            
            settingsWindow.Show();
        }

        private void ExitApp()
        {
            // Unregister notification service
            try
            {
                var notificationService = _host?.Services.GetService<INotificationService>();
                notificationService?.Unregister();
            }
            catch { }
            
            Application.Current.Shutdown();
        }

        private void OnNotificationClicked(object? sender, NotificationClickedEventArgs e)
        {
            var logger = _host?.Services.GetService<ILogger<App>>();
            logger?.LogInformation("Notification clicked - Action: {Action}, Data: {Data}", e.Action, e.Data);
            
            if (e.Action == "showProvider" && !string.IsNullOrEmpty(e.Data))
            {
                // Show dashboard and bring to front
                Dispatcher.Invoke(async () =>
                {
                    await ShowDashboard();
                    // TODO: Highlight or scroll to specific provider in dashboard
                });
            }
        }

        private MainWindow? _mainWindow;

        private async Task ShowDashboard()
        {
            if (_mainWindow == null)
            {
                // Create Window
                _mainWindow = _host?.Services.GetRequiredService<MainWindow>();
                
                if (_mainWindow != null)
                {
                    // Preload Preferences to prevent race condition on Deactivated event
                    var loader = _host?.Services.GetRequiredService<IConfigLoader>();
                    if (loader != null)
                    {
                        var prefs = await loader.LoadPreferencesAsync(); 
                        _mainWindow.SetInitialPreferences(prefs);
                    }

                    _mainWindow.Closed += (s, e) => _mainWindow = null;
                    _mainWindow.Show();
                    _mainWindow.Activate();
                }
            }
            else
            {
                if (_mainWindow.Visibility == Visibility.Visible && _mainWindow.IsActive)
                {
                    _mainWindow.Hide();
                }
                else
                {
                    _mainWindow.Show();
                    _mainWindow.Activate();
                    // Ensure it's not minimized
                    if (_mainWindow.WindowState == WindowState.Minimized)
                        _mainWindow.WindowState = WindowState.Normal;
                    // Re-apply Topmost setting to ensure window stays on top
                    var loader = _host?.Services.GetRequiredService<IConfigLoader>();
                    if (loader != null)
                    {
                        var prefs = await loader.LoadPreferencesAsync();
                        _mainWindow.Topmost = prefs.AlwaysOnTop;
                    }
                }
            }
        }

        private void ShowInfo()
        {
            var infoDialog = Services.GetRequiredService<InfoDialog>();
            infoDialog.Owner = _mainWindow;
            infoDialog.ShowDialog();
        }

        public void UpdateProviderTrayIcons(List<ProviderUsage> usages, List<ProviderConfig> configs, AppPreferences? prefs = null)
        {
            var desiredIcons = new Dictionary<string, (string ToolTip, double Percentage, bool IsQuota)>();
            int yellowThreshold = prefs?.ColorThresholdYellow ?? 60;
            int redThreshold = prefs?.ColorThresholdRed ?? 80;

            foreach (var config in configs)
            {
                var usage = usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
                if (usage == null) continue;

                // Main Tray
                if (config.ShowInTray)
                {
                    var isQuota = usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota;
                    desiredIcons[config.ProviderId] = (
                        $"{usage.ProviderName}: {usage.Description}",
                        usage.UsagePercentage,
                        isQuota
                    );
                }

                // Sub Trays (e.g. Antigravity credits or specific models)
                if (config.EnabledSubTrays != null && usage.Details != null)
                {
                    foreach (var subName in config.EnabledSubTrays)
                    {
                        var detail = usage.Details.FirstOrDefault(d => d.Name.Equals(subName, StringComparison.OrdinalIgnoreCase));
                        if (detail != null)
                        {
                            // Parse percentage from "85%" style string in Used property
                            double pct = 0;
                            var usedStr = detail.Used.TrimEnd('%');
                            if (double.TryParse(usedStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedPct)) 
                            {
                                pct = parsedPct;
                            }

                            var key = $"{config.ProviderId}:{subName}";
                            var isQuotaSub = usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota;
                            desiredIcons[key] = (
                                $"{usage.ProviderName} - {subName}: {detail.Description} ({detail.Used})",
                                pct,
                                isQuotaSub
                            );
                        }
                    }
                }
            }

            // 1. Remove icons no longer in desiredIcons
            var currentKeys = _providerTrayIcons.Keys.ToList();
            foreach (var key in currentKeys)
            {
                if (!desiredIcons.ContainsKey(key))
                {
                    _providerTrayIcons[key].Dispose();
                    _providerTrayIcons.Remove(key);
                }
            }

            // 2. Add or update desired icons
            foreach (var kvp in desiredIcons)
            {
                var key = kvp.Key;
                var info = kvp.Value;

                if (!_providerTrayIcons.ContainsKey(key))
                {
                    var tray = new TaskbarIcon();
                    tray.ToolTipText = info.ToolTip;
                    tray.IconSource = GenerateUsageIcon(info.Percentage, yellowThreshold, redThreshold, prefs?.InvertProgressBar ?? false, info.IsQuota);
                    tray.TrayLeftMouseDown += async (s, e) => await ShowDashboard();
                    tray.TrayMouseDoubleClick += async (s, e) => await ShowDashboard();
                    _providerTrayIcons.Add(key, tray);
                }
                else
                {
                    var tray = _providerTrayIcons[key];
                    tray.ToolTipText = info.ToolTip;
                    tray.IconSource = GenerateUsageIcon(info.Percentage, yellowThreshold, redThreshold, prefs?.InvertProgressBar ?? false, info.IsQuota);
                }
            }
        }

        private System.Windows.Media.ImageSource GenerateUsageIcon(double percentage, int yellowThreshold, int redThreshold, bool invert = false, bool isQuota = false)
        {
            int size = 32; 
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Background (Dark)
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 20)), null, new Rect(0, 0, size, size));
                
                // Outer Border
                dc.DrawRectangle(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.DimGray, 1), new Rect(0.5, 0.5, size - 1, size - 1));

                // Fill logic
                // For quota-based providers: percentage represents REMAINING, so invert thresholds
                // High Remaining (> YellowThreshold) -> MediumSeaGreen (Good)
                // Mid Remaining (> RedThreshold) -> Gold (Warning)
                // Low Remaining (< RedThreshold) -> Crimson (Danger)
                // For usage-based: percentage represents USED
                // High Usage (> RedThreshold) -> Crimson (Danger)
                // Mid Usage (> YellowThreshold) -> Gold (Warning)
                // Low Usage -> MediumSeaGreen (Good)
                var fillBrush = isQuota
                    ? (percentage < (100 - redThreshold) ? System.Windows.Media.Brushes.Crimson : (percentage < (100 - yellowThreshold) ? System.Windows.Media.Brushes.Gold : System.Windows.Media.Brushes.MediumSeaGreen))
                    : (percentage > redThreshold ? System.Windows.Media.Brushes.Crimson : (percentage > yellowThreshold ? System.Windows.Media.Brushes.Gold : System.Windows.Media.Brushes.MediumSeaGreen));
                
                double barWidth = size - 6;
                double barHeight = size - 6;
                double fillHeight;
                if (invert)
                {
                    // Invert: Height represents REMAINING. 
                    // 0 used = 100% height (Full). 
                    // 100 used = 0% height (Empty).
                    // We draw from bottom up.
                    double remaining = Math.Max(0, 100.0 - percentage);
                    fillHeight = (remaining / 100.0) * barHeight;
                }
                else
                {
                    // Standard: Height represents USED.
                    fillHeight = (percentage / 100.0) * barHeight;
                }

                // Draw Bar
                dc.DrawRectangle(fillBrush, null, new Rect(3, size - 3 - fillHeight, barWidth, fillHeight));
            }

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _taskbarIcon?.Dispose();
            foreach (var tray in _providerTrayIcons.Values) tray.Dispose();
            _providerTrayIcons.Clear();

            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
            base.OnExit(e);
            
            // Force kill to prevent zombie processes (threads, locked files etc)
            Process.GetCurrentProcess().Kill();
        }
    }
}