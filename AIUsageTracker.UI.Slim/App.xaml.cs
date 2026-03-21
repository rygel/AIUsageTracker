// <copyright file="App.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Configuration;
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
    private const int SingleInstanceLockWaitMilliseconds = 250;

    private static IHost? _host;
    private readonly Dictionary<string, TaskbarIcon> _providerTrayIcons = new(StringComparer.Ordinal);
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

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

    public Func<Window> InfoDialogFactory { get; set; } = () => new InfoDialog();

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
        if (!this.TryAcquireSingleInstanceLock())
        {
            base.OnStartup(e);
            this.Shutdown(0);
            return;
        }

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
        catch (Exception ex)
        {
            UiDiagnosticFileLog.Write($"[DIAGNOSTIC] Failed to load preferences on startup: {ex.Message}");
            App.ApplyTheme(AppTheme.Dark);
        }

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
            await Host.StopAsync();
        }

        this.ReleaseSingleInstanceLock();
        base.OnExit(e);
    }
#pragma warning restore VSTHRD100

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<IAppPathProvider, AIUsageTracker.Infrastructure.Helpers.DefaultAppPathProvider>();
        services.AddSingleton<UiPreferencesStore>();
        services.AddSingleton<DisplayPreferencesService>();
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<IMonitorLifecycleService, MonitorLifecycleService>();
        services.AddSingleton<IUsageAnalyticsService, NoOpUsageAnalyticsService>();
        services.AddSingleton<IDataExportService, NoOpDataExportService>();
        services.AddSingleton<IUpdateCheckerService, GitHubUpdateChecker>();
        services.AddSingleton<HttpClient>();
        services.AddHttpClient("LocalhostProbe")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(1));

        // UI Services
        services.AddSingleton<IWindowBehaviorService, WindowBehaviorService>();
        services.AddSingleton<IErrorDisplayService, ErrorDisplayService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IPollingService, PollingService>();
        services.AddSingleton<IReactivePollingService, ReactivePollingService>();
        services.AddSingleton<IMonitorStartupOrchestrator, MonitorStartupOrchestrator>();
        services.AddSingleton<IBrowserService, BrowserService>();

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

    private bool TryAcquireSingleInstanceLock()
    {
        this._singleInstanceMutex ??= new Mutex(initiallyOwned: false, name: BuildSingleInstanceMutexName());

        try
        {
            this._ownsSingleInstanceMutex = this._singleInstanceMutex.WaitOne(
                TimeSpan.FromMilliseconds(SingleInstanceLockWaitMilliseconds));
        }
        catch (AbandonedMutexException)
        {
            this._ownsSingleInstanceMutex = true;
            UiDiagnosticFileLog.Write("[DIAGNOSTIC] Slim UI single-instance lock was abandoned; continuing.");
        }

        if (this._ownsSingleInstanceMutex)
        {
            UiDiagnosticFileLog.Write("[DIAGNOSTIC] Slim UI single-instance lock acquired.");
            return true;
        }

        const string duplicateLaunchMessage = "[DIAGNOSTIC] Duplicate Slim UI launch detected; exiting second instance.";
        UiDiagnosticFileLog.Write(duplicateLaunchMessage);
        Debug.WriteLine(duplicateLaunchMessage);
        return false;
    }

    private void ReleaseSingleInstanceLock()
    {
        if (this._ownsSingleInstanceMutex && this._singleInstanceMutex != null)
        {
            try
            {
                this._singleInstanceMutex.ReleaseMutex();
                UiDiagnosticFileLog.Write("[DIAGNOSTIC] Slim UI single-instance lock released.");
            }
            catch (ApplicationException ex)
            {
                UiDiagnosticFileLog.Write($"[DIAGNOSTIC] Failed to release Slim UI lock: {ex.Message}");
            }
        }

        this._singleInstanceMutex?.Dispose();
        this._singleInstanceMutex = null;
        this._ownsSingleInstanceMutex = false;
    }

    private static string BuildSingleInstanceMutexName()
    {
        var userName = Environment.UserName
            .Replace("\\", "_", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);

        return @"Local\AIUsageTracker_SlimUI_" + userName;
    }
}
