using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Infrastructure.Configuration;
using AIConsumptionTracker.Infrastructure.Providers;
using AIConsumptionTracker.Infrastructure.Helpers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIConsumptionTracker.UI
{
    public partial class App : Application
    {
        private TaskbarIcon? _taskbarIcon;
        private readonly Dictionary<string, TaskbarIcon> _providerTrayIcons = new();
        private IHost? _host;
        public IServiceProvider Services => _host!.Services;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create Icon (Lazy way: we might want a simple icon resource)
            // Ideally we need an .ico file. For now, we might crash if we don't have one? 
            // Hardcodet requires an IconSource.
            
            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging => 
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddHttpClient(); // Required for providers
                    services.AddSingleton<IConfigLoader, JsonConfigLoader>();
                    
                    // Register Providers
                    services.AddTransient<IProviderService, SimulatedProvider>(); 
                    services.AddTransient<IProviderService, OpenCodeProvider>();
                    services.AddTransient<IProviderService, ZaiProvider>();
                    services.AddTransient<IProviderService, OpenRouterProvider>();
                    services.AddTransient<IProviderService, AntigravityProvider>();
                    services.AddTransient<IProviderService, GeminiProvider>();
                    services.AddTransient<IProviderService, KimiProvider>();
                    services.AddTransient<IProviderService, OpenCodeZenProvider>();
                    services.AddTransient<IProviderService, DeepSeekProvider>();
                    services.AddTransient<IProviderService, OpenAIProvider>();
                    services.AddTransient<IProviderService, AnthropicProvider>();
                    services.AddTransient<IProviderService, CloudCodeProvider>();
                    services.AddTransient<IProviderService, GenericPayAsYouGoProvider>();
                    services.AddTransient<IProviderService, GitHubCopilotProvider>();
                    
                    services.AddSingleton<WindowsBrowserCookieService>();
                    services.AddSingleton<ProviderManager>();
                    services.AddTransient<MainWindow>(); // Dashboard
                    services.AddTransient<SettingsWindow>();
                })
                .Build();

            await _host.StartAsync();
            
            // Preload data
            var providerManager = _host.Services.GetRequiredService<ProviderManager>();
            _ = Task.Run(() => providerManager.GetAllUsageAsync(forceRefresh: true)); // Fire and forget preload on thread pool

            InitializeTrayIcon();
            ShowDashboard();
        }

        private void InitializeTrayIcon()
        {
            _taskbarIcon = new TaskbarIcon();
            _taskbarIcon.ToolTipText = "AI Consumption Tracker";
            _taskbarIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app_icon.png"));

            // Context Menu
            var contextMenu = new ContextMenu();
            
            var openItem = new MenuItem { Header = "Open" };
            openItem.Click += (s, e) => ShowDashboard();
            
            var settingsItem = new MenuItem { Header = "Settings" };
            settingsItem.Click += (s, e) => ShowSettings();
            
            var exitItem = new MenuItem { Header = "Exit" };
            exitItem.Click += (s, e) => ExitApp();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = contextMenu;

            // Wire up single click and double click to show dashboard
            _taskbarIcon.TrayLeftMouseDown += (s, e) => ShowDashboard();
            _taskbarIcon.TrayMouseDoubleClick += (s, e) => ShowDashboard();
        }

        private void ShowSettings()
        {
            // Ensure dashboard is created
            if (_mainWindow == null)
            {
                ShowDashboard();
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
                 // Refresh when settings closes
                 if (_mainWindow != null && _mainWindow.IsVisible && _mainWindow is MainWindow main)
                 {
                     await main.RefreshData(forceRefresh: true);
                 }
            };
            
            settingsWindow.Show();
        }

        private void ExitApp()
        {
            Application.Current.Shutdown();
        }

        private MainWindow? _mainWindow;

        private void ShowDashboard()
        {
            if (_mainWindow == null)
            {
                // Create Window
                _mainWindow = _host?.Services.GetRequiredService<MainWindow>();
                
                if (_mainWindow != null)
                {
                    // Preload Preferences to prevent race condition on Deactivated event
                    // We fire-and-forget this specifically to get the task but we want to await it if possible? 
                    // Actually, we can just run it synchronously-ish or use the ConfigLoader directly since we are on UI thread.
                    // But ConfigLoader is async.
                    
                    var loader = _host?.Services.GetRequiredService<IConfigLoader>();
                    if (loader != null)
                    {
                        // We must wait for this or the window will show with default (StayOpen=false)
                        // triggering the bug if user clicks away instantly.
                        var prefs = loader.LoadPreferencesAsync().GetAwaiter().GetResult(); 
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
                }
            }
        }

        public void UpdateProviderTrayIcons(List<ProviderUsage> usages, List<ProviderConfig> configs, AppPreferences? prefs = null)
        {
            var desiredIcons = new Dictionary<string, (string ToolTip, double Percentage)>();
            int yellowThreshold = prefs?.ColorThresholdYellow ?? 60;
            int redThreshold = prefs?.ColorThresholdRed ?? 80;

            foreach (var config in configs)
            {
                var usage = usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
                if (usage == null) continue;

                // Main Tray
                if (config.ShowInTray)
                {
                    desiredIcons[config.ProviderId] = (
                        $"{usage.ProviderName}: {usage.Description}",
                        usage.UsagePercentage
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
                            desiredIcons[key] = (
                                $"{usage.ProviderName} - {subName}: {detail.Description} ({detail.Used})",
                                pct
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
                    tray.IconSource = GenerateUsageIcon(info.Percentage, yellowThreshold, redThreshold, prefs?.InvertProgressBar ?? false);
                    tray.TrayLeftMouseDown += (s, e) => ShowDashboard();
                    tray.TrayMouseDoubleClick += (s, e) => ShowDashboard();
                    _providerTrayIcons.Add(key, tray);
                }
                else
                {
                    var tray = _providerTrayIcons[key];
                    tray.ToolTipText = info.ToolTip;
                    tray.IconSource = GenerateUsageIcon(info.Percentage, yellowThreshold, redThreshold, prefs?.InvertProgressBar ?? false);
                }
            }
        }

        private System.Windows.Media.ImageSource GenerateUsageIcon(double percentage, int yellowThreshold, int redThreshold, bool invert = false)
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
                var fillBrush = percentage > redThreshold ? System.Windows.Media.Brushes.Crimson : (percentage > yellowThreshold ? System.Windows.Media.Brushes.Gold : System.Windows.Media.Brushes.MediumSeaGreen);
                
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
        }
    }
}

