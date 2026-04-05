// <copyright file="App.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net.Http;
using System.Windows;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.UI.Slim.Services;
using AIUsageTracker.UI.Slim.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class App : Application
{
    private static IHost? _host;
    private readonly Dictionary<string, TaskbarIcon> _providerTrayIcons = new(StringComparer.Ordinal);
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private SingleInstanceLockService? _singleInstanceLockService;

    /// <summary>
    /// Gets the background task that ensures the monitor is running.
    /// Fired immediately on startup so it runs in parallel with WPF initialization.
    /// </summary>
    public static Task<bool> MonitorWarmupTask { get; private set; } = Task.FromResult(false);

    public App()
    {
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services);
            })
            .Build();
    }

    public static event EventHandler<PrivacyChangedEventArgs>? PrivacyChanged;

    public static IHost Host => _host ?? throw new InvalidOperationException("App host is not initialized.");

    public static IMonitorService MonitorService => Host.Services.GetRequiredService<IMonitorService>();

    public static AppPreferences Preferences { get; set; } = new();

    public static bool IsPrivacyMode { get; set; }

    public Func<bool> IsMainWindowVisible { get; set; } = () => Current.MainWindow?.IsVisible ?? false;

    public Func<Window> InfoDialogFactory { get; set; } = () => Host.Services.GetRequiredService<InfoDialog>();

    public Action<Window> ShowInfoDialogAction { get; set; } = dialog => dialog.ShowDialog();

    public static void SetPrivacyMode(bool enabled)
    {
        IsPrivacyMode = enabled;
        Preferences.IsPrivacyMode = enabled;
        PrivacyChanged?.Invoke(null, new PrivacyChangedEventArgs(enabled));
    }

    public static ILogger<T> CreateLogger<T>() => Host.Services.GetRequiredService<ILogger<T>>();

    // Testing Support
    public void SetMainWindowForTesting(MainWindow window) => this._mainWindow = window;

    public void OpenInfoDialog() => this.ShowInfoDialogAction(this.InfoDialogFactory());

#pragma warning disable VSTHRD100 // WPF Application lifecycle overrides require async void signatures
    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        this._singleInstanceLockService = Host.Services.GetRequiredService<SingleInstanceLockService>();
        if (!this._singleInstanceLockService.TryAcquire())
        {
            base.OnStartup(e);
            this.Shutdown(0);
            return;
        }

        await Host.StartAsync().ConfigureAwait(true);
        base.OnStartup(e);

        if (e.Args.Contains("--test", StringComparer.OrdinalIgnoreCase) &&
            e.Args.Contains("--screenshot", StringComparer.OrdinalIgnoreCase))
        {
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = this.RunHeadlessScreenshotCaptureAsync(e.Args);
            return;
        }

        // Fire monitor warmup IMMEDIATELY — runs in parallel with preferences load,
        // theme apply, tray icon init, and the expensive MainWindow InitializeComponent.
        // By the time the window is shown, the monitor should already be running.
        MonitorWarmupTask = Task.Run(async () =>
        {
            try
            {
                var lifecycle = Host.Services.GetRequiredService<MonitorLifecycleService>();
                return await lifecycle.EnsureAgentRunningAsync().ConfigureAwait(false); // ui-thread-guardrail-allow: Task.Run thread pool
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Host.Services.GetRequiredService<ILogger<App>>()
                    .LogWarning(ex, "Background monitor warmup failed");
                return false;
            }
        });

        var preferencesStore = Host.Services.GetRequiredService<UiPreferencesStore>();
        try
        {
            Preferences = await preferencesStore.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var logger = Host.Services.GetRequiredService<ILogger<App>>();
            logger.LogError(ex, "Failed to load preferences from disk");
            MessageBox.Show(
                $"Could not load preferences:\n{ex.Message}\n\nThe application will start with default settings.",
                "Preferences Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Preferences = new AppPreferences();
        }

        ApplyTheme(Preferences.Theme);
        IsPrivacyMode = Preferences.IsPrivacyMode;

        this.InitializeTrayIcon();

        this._mainWindow = Host.Services.GetRequiredService<MainWindow>();
        this._mainWindow.Show();
    }

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
            await Host.StopAsync().ConfigureAwait(true);
        }

        this._singleInstanceLockService?.Release();
        base.OnExit(e);
    }
#pragma warning restore VSTHRD100

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<IAppPathProvider, AIUsageTracker.Infrastructure.Helpers.DefaultAppPathProvider>();
        services.AddSingleton<UiPreferencesStore>();
        services.AddSingleton<IUiPreferencesStore>(sp => sp.GetRequiredService<UiPreferencesStore>());
        services.AddSingleton<IMonitorLauncher, MonitorLauncher>();
        services.AddSingleton<MonitorLauncher>();
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<MonitorLifecycleService>();
        services.AddSingleton<GitHubUpdateChecker>(sp =>
            new GitHubUpdateChecker(
                sp.GetRequiredService<ILogger<GitHubUpdateChecker>>(),
                sp.GetRequiredService<HttpClient>(),
                Preferences.UpdateChannel));
        services.AddSingleton<Func<UpdateChannel, GitHubUpdateChecker>>(sp => channel =>
            new GitHubUpdateChecker(
                sp.GetRequiredService<ILogger<GitHubUpdateChecker>>(),
                sp.GetRequiredService<HttpClient>(),
                channel));
        services.AddSingleton<HttpClient>();
        services.AddHttpClient("LocalhostProbe")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(1));

        // UI Services
        services.AddSingleton<SingleInstanceLockService>();
        services.AddSingleton<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
        services.AddSingleton<Func<InfoDialog>>(sp => () => sp.GetRequiredService<InfoDialog>());
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MonitorStartupOrchestrator>();
        services.AddSingleton<IBrowserService, BrowserService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<InfoDialog>();

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
}
