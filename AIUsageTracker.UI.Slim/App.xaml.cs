// <copyright file="App.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim
{
    using System.Windows;
    using Hardcodet.Wpf.TaskbarNotification;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.MonitorClient;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Infrastructure.Configuration;
    using AIUsageTracker.Infrastructure.Services;
    using AIUsageTracker.UI.Slim.ViewModels;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System.IO;
    using System.Net.Http;
    using System.Windows.Media;

    public partial class App : Application
    {
        private static IHost? _host;
        public static IHost Host => _host ?? throw new InvalidOperationException("App host is not initialized.");

        public static IMonitorService MonitorService => Host.Services.GetRequiredService<IMonitorService>();
        public static AppPreferences Preferences { get; set; } = new();
        public static bool IsPrivacyMode { get; set; } = false;

        public static ILogger<T> CreateLogger<T>() => Host.Services.GetRequiredService<ILogger<T>>();

        private TaskbarIcon? _trayIcon;
        private readonly Dictionary<string, TaskbarIcon> _providerTrayIcons = new();
        private MainWindow? _mainWindow;

        public static event EventHandler<bool>? PrivacyChanged;

        public App()
        {
            _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services);
                })
                .Build();
        }
    `n
        private static void ConfigureServices(IServiceCollection services)
        {
            // Infrastructure
            services.AddSingleton<IAppPathProvider, AIUsageTracker.Infrastructure.Helpers.DefaultAppPathProvider>();
            services.AddSingleton<UiPreferencesStore>();
            services.AddSingleton<IMonitorService, MonitorService>();
            services.AddSingleton<IUsageAnalyticsService, NoOpUsageAnalyticsService>();
            services.AddSingleton<IDataExportService, NoOpDataExportService>();
            services.AddSingleton<IUpdateCheckerService, GitHubUpdateChecker>();
            services.AddSingleton<HttpClient>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<SettingsViewModel>();

            // Windows
            services.AddSingleton<MainWindow>();
            services.AddTransient<SettingsWindow>();

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSimpleConsole(options =>
                {
                    options.TimestampFormat = "HH:mm:ss.fff ";
                    options.SingleLine = true;
                });
                builder.AddDebug();
            });
        }
    `n
        protected override async void OnStartup(StartupEventArgs e)
        {
            await Host.StartAsync();
            base.OnStartup(e);

            if (e.Args.Contains("--test", StringComparer.OrdinalIgnoreCase) &&
                e.Args.Contains("--screenshot", StringComparer.OrdinalIgnoreCase))
            {
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = this.RunHeadlessScreenshotCaptureAsync(e.Args);
                return;
            }

            try
            {
                var preferencesStore = Host.Services.GetRequiredService<UiPreferencesStore>();
                Preferences = await preferencesStore.LoadAsync();
                App.ApplyTheme(Preferences.Theme);
                IsPrivacyMode = Preferences.IsPrivacyMode;
            }
            catch
            {
                App.ApplyTheme(AppTheme.Dark);
            }

            this.InitializeTrayIcon();

            this._mainWindow = Host.Services.GetRequiredService<MainWindow>();
            this._mainWindow.Show();
        }
    `n
        protected override async void OnExit(ExitEventArgs e)
        {
            this._trayIcon?.Dispose();
            foreach (var tray in this._providerTrayIcons.Values)
            {
                tray.Dispose();
            }
            this._providerTrayIcons.Clear();

            using (_host)
            {
                await Host.StopAsync();
            }
            base.OnExit(e);
        }
    `n
        public static void SetPrivacyMode(bool enabled)
        {
            IsPrivacyMode = enabled;
            Preferences.IsPrivacyMode = enabled;
            PrivacyChanged?.Invoke(null, enabled);
        }

        // Testing Support
        public void SetMainWindowForTesting(MainWindow window) => this._mainWindow = window;
        public Func<bool> IsMainWindowVisible { get; set; } = () => Current.MainWindow?.IsVisible ?? false;
        public Func<Window> InfoDialogFactory { get; set; } = () => new InfoDialog();
        public Action<Window> ShowInfoDialogAction { get; set; } = dialog => dialog.ShowDialog();
        public void OpenInfoDialog() => this.ShowInfoDialogAction(this.InfoDialogFactory());
    }
    `n
    public class NoOpUsageAnalyticsService : IUsageAnalyticsService
    {
        public Task<IReadOnlyDictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(IEnumerable<string> providerIds, int lookbackHours = 24, int maxSamplesPerProvider = 100) => Task.FromResult<IReadOnlyDictionary<string, BurnRateForecast>>(new Dictionary<string, BurnRateForecast>());
        public Task<IReadOnlyDictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(IEnumerable<string> providerIds, int lookbackHours = 24, int maxSamplesPerProvider = 100) => Task.FromResult<IReadOnlyDictionary<string, ProviderReliabilitySnapshot>>(new Dictionary<string, ProviderReliabilitySnapshot>());
        public Task<IReadOnlyDictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(IEnumerable<string> providerIds, int lookbackHours = 24, int maxSamplesPerProvider = 100) => Task.FromResult<IReadOnlyDictionary<string, UsageAnomalySnapshot>>(new Dictionary<string, UsageAnomalySnapshot>());
        public Task<IReadOnlyList<UsageComparison>> GetUsageComparisonsAsync(IEnumerable<string> providerIds) => Task.FromResult<IReadOnlyList<UsageComparison>>(new List<UsageComparison>());
        public Task<IReadOnlyList<BudgetStatus>> GetBudgetStatusesAsync(IEnumerable<string> providerIds) => Task.FromResult<IReadOnlyList<BudgetStatus>>(new List<BudgetStatus>());
    }
    `n
    public class NoOpDataExportService : IDataExportService
    {
        public Task<string> ExportHistoryToCsvAsync() => Task.FromResult(string.Empty);
        public Task<string> ExportHistoryToJsonAsync() => Task.FromResult(string.Empty);
        public Task<byte[]?> CreateDatabaseBackupAsync() => Task.FromResult<byte[]?>(null);
    }

}
