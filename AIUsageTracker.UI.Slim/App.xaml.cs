using System.Windows;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim.Interfaces;
using AIUsageTracker.UI.Slim.Services;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.UI.Slim.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace AIUsageTracker.UI.Slim;

public partial class App : Application
{
    private static IHost? _host;
    public static IHost Host => _host ?? throw new InvalidOperationException("App host is not initialized.");

    public static IMonitorService AppMonitorService => Host.Services.GetRequiredService<IMonitorService>();
    public static AppPreferences Preferences { get; set; } = new();
    public static bool IsPrivacyMode { get; set; } = false;

    public static ILogger<T> CreateLogger<T>() => Host.Services.GetRequiredService<ILogger<T>>();
    
    private ITrayIconService? _trayIconService;
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
        // Infrastructure & Paths
        services.AddSingleton<IAppPathProvider, AIUsageTracker.Infrastructure.Helpers.DefaultAppPathProvider>();
        services.AddSingleton<UiPreferencesStore>();
        
        // UI Services
        services.AddSingleton<IThemeService, WpfThemeService>();
        services.AddSingleton<IScreenshotService, WpfScreenshotService>();
        services.AddSingleton<ITrayIconService, WpfTrayIconService>();
        
        // Monitor Client
        services.AddSingleton<IMonitorService, AIUsageTracker.Core.MonitorClient.MonitorService>();
        services.AddSingleton<IUpdateCheckerService, GitHubUpdateChecker>();
        services.AddSingleton<HttpClient>();
        
        // Mock/Fallback Services
        services.AddSingleton<IUsageAnalyticsService, NoOpUsageAnalyticsService>();
        services.AddSingleton<IDataExportService, NoOpDataExportService>();
        
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
            var screenshotService = Host.Services.GetRequiredService<IScreenshotService>();
            _ = screenshotService.RunHeadlessScreenshotCaptureAsync(e.Args);
            return;
        }

        try
        {
            var preferencesStore = Host.Services.GetRequiredService<UiPreferencesStore>();
            Preferences = await preferencesStore.LoadAsync();
            
            var themeService = Host.Services.GetRequiredService<IThemeService>();
            themeService.ApplyTheme(Preferences.Theme);
            
            IsPrivacyMode = Preferences.IsPrivacyMode;
        }
        catch
        {
            var themeService = Host.Services.GetRequiredService<IThemeService>();
            themeService.ApplyTheme(AppTheme.Dark);
        }

        _trayIconService = Host.Services.GetRequiredService<ITrayIconService>();
        _trayIconService.Initialize();

        _mainWindow = Host.Services.GetRequiredService<MainWindow>();
        _mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();

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

    // Static delegation for legacy code or simple access
    public static void ApplyTheme(AppTheme theme) => Host.Services.GetRequiredService<IThemeService>().ApplyTheme(theme);
    public static void ApplyTheme(Window window) => Host.Services.GetRequiredService<IThemeService>().ApplyTheme(window);
    public static void ApplyTheme(Window window, string themeName) => Host.Services.GetRequiredService<IThemeService>().ApplyTheme(window, themeName);
    public static void RenderWindowContent(Window window, string outputPath) => Host.Services.GetRequiredService<IScreenshotService>().RenderWindowContent(window, outputPath);
    
    public void UpdateProviderTrayIcons(List<ProviderUsage> usages, List<ProviderConfig> configs, AppPreferences? prefs = null)
    {
        _trayIconService?.UpdateProviderTrayIcons(usages, configs, prefs);
    }

    // Testing Support
    public void SetMainWindowForTesting(MainWindow window) => _mainWindow = window;
    public Func<bool> IsMainWindowVisible { get; set; } = () => Current.MainWindow?.IsVisible ?? false;
    public Func<Window> InfoDialogFactory { get; set; } = () => new InfoDialog();
    public Action<Window> ShowInfoDialogAction { get; set; } = dialog => dialog.ShowDialog();
    public void OpenInfoDialog() => ShowInfoDialogAction(InfoDialogFactory());
}
