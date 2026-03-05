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

namespace AIUsageTracker.UI.Slim;

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

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<IMonitorService, MonitorService>();
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        await Host.StartAsync();
        base.OnStartup(e);

        if (e.Args.Contains("--test", StringComparer.OrdinalIgnoreCase) &&
            e.Args.Contains("--screenshot", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunHeadlessScreenshotCaptureAsync(e.Args);
            return;
        }

        try
        {
            Preferences = await UiPreferencesStore.LoadAsync();
            ApplyTheme(Preferences.Theme);
            IsPrivacyMode = Preferences.IsPrivacyMode;
        }
        catch
        {
            ApplyTheme(AppTheme.Dark);
        }

        InitializeTrayIcon();

        _mainWindow = Host.Services.GetRequiredService<MainWindow>();
        _mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        foreach (var tray in _providerTrayIcons.Values)
        {
            tray.Dispose();
        }
        _providerTrayIcons.Clear();

        using (_host)
        {
            await Host.StopAsync();
        }
        base.OnExit(e);
    }

    public static void SetPrivacyMode(bool enabled)
    {
        IsPrivacyMode = enabled;
        Preferences.IsPrivacyMode = enabled;
        PrivacyChanged?.Invoke(null, enabled);
    }

    // Testing Support
    public void SetMainWindowForTesting(MainWindow window) => _mainWindow = window;
    public Func<bool> IsMainWindowVisible { get; set; } = () => Current.MainWindow?.IsVisible ?? false;
    public Func<Window> InfoDialogFactory { get; set; } = () => new InfoDialog();
    public Action<Window> ShowInfoDialogAction { get; set; } = dialog => dialog.ShowDialog();
    public void OpenInfoDialog() => ShowInfoDialogAction(InfoDialogFactory());
}

