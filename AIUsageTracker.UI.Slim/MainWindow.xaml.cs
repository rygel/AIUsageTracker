using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using AIUsageTracker.UI.Slim.ViewModels;
using SharpVectors.Renderers.Wpf;
using SharpVectors.Converters;

namespace AIUsageTracker.UI.Slim;

public enum StatusType
{
    Info,
    Success,
    Warning,
    Error
}

public partial class MainWindow : Window
{
    private static readonly Regex MarkdownTokenRegex = new(
        @"(\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*|\[[^\]]+\]\([^)]+\))",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    private readonly MainViewModel _viewModel;
    private readonly IMonitorService _monitorService;
    private readonly ILogger<MainWindow> _logger;
    private readonly UiPreferencesStore _preferencesStore;
    private IUpdateCheckerService _updateChecker;
    private AppPreferences _preferences = new();
    private List<ProviderUsage> _usages = new();
    private List<ProviderConfig> _configs = new();
    private bool _isPrivacyMode = App.IsPrivacyMode;
    private bool _isLoading = false;
    private readonly Dictionary<string, ImageSource> _iconCache = new();
    private DateTime _lastMonitorUpdate = DateTime.MinValue;
    private DateTime _lastRefreshTrigger = DateTime.MinValue;
    private const int RefreshCooldownSeconds = 120;
    private static readonly TimeSpan StartupPollingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NormalPollingInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TrayConfigRefreshInterval = TimeSpan.FromMinutes(5);
    private bool _isPollingInProgress;
    private bool _isTrayIconUpdateInProgress;
    private DispatcherTimer? _pollingTimer;
    private DateTime _lastTrayConfigRefresh = DateTime.MinValue;
    private string? _monitorContractWarningMessage;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly DispatcherTimer _alwaysOnTopTimer;
    private HwndSource? _windowSource;
    private UpdateInfo? _latestUpdate;
    private bool _preferencesLoaded;
    private int _topmostRecoveryGeneration;
    private bool _isSettingsDialogOpen;
    private bool _isTooltipOpen;
    internal Func<(Window Dialog, Func<bool> HasChanges)> SettingsDialogFactory { get; set; } = CreateDefaultSettingsDialog;
    internal Func<Window, bool?> ShowOwnedDialog { get; set; } = static dialog => dialog.ShowDialog();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public MainWindow(
        MainViewModel viewModel,
        IMonitorService monitorService,
        ILogger<MainWindow> logger,
        IUpdateCheckerService updateChecker,
        UiPreferencesStore preferencesStore)
        : this(skipUiInitialization: false, viewModel, monitorService, logger, updateChecker, preferencesStore)
    {
    }

    public MainWindow()
        : this(App.Host.Services.GetRequiredService<MainViewModel>(),
               App.Host.Services.GetRequiredService<IMonitorService>(),
               App.Host.Services.GetRequiredService<ILogger<MainWindow>>(),
               App.Host.Services.GetRequiredService<IUpdateCheckerService>(),
               App.Host.Services.GetRequiredService<UiPreferencesStore>())
    {
    }

    internal MainWindow(bool skipUiInitialization)
        : this(skipUiInitialization, null, null, null, null, null)
    {
    }

    private MainWindow(
        bool skipUiInitialization,
        MainViewModel? viewModel,
        IMonitorService? monitorService,
        ILogger<MainWindow>? logger,
        IUpdateCheckerService? updateChecker,
        UiPreferencesStore? preferencesStore)
    {
        if (!skipUiInitialization)
        {
            InitializeComponent();
            ApplyVersionDisplay();
        }

        // Fallbacks for internal/test use
        _logger = logger ?? App.CreateLogger<MainWindow>();
        _monitorService = monitorService ?? App.MonitorService;
        _updateChecker = updateChecker ?? App.Host.Services.GetRequiredService<IUpdateCheckerService>();
        _preferencesStore = preferencesStore ?? App.Host.Services.GetRequiredService<UiPreferencesStore>();
        _viewModel = viewModel ?? App.Host.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;
        
        _updateCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        _alwaysOnTopTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        if (skipUiInitialization)
        {
            return;
        }

        _updateCheckTimer.Tick += async (s, e) =>
        {
            try
            {
                await CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateCheckTimer_Tick failed");
            }
        };
        _updateCheckTimer.Start();
        _alwaysOnTopTimer.Tick += (s, e) => EnsureAlwaysOnTop();
        _alwaysOnTopTimer.Start();

        SourceInitialized += OnSourceInitialized;
        App.PrivacyChanged += OnPrivacyChanged;
        Closed += (s, e) =>
        {
            App.PrivacyChanged -= OnPrivacyChanged;
            _updateCheckTimer.Stop();
            _alwaysOnTopTimer.Stop();
            SourceInitialized -= OnSourceInitialized;

            if (_windowSource is not null)
            {
                _windowSource.RemoveHook(WndProc);
                _windowSource = null;
            }
        };
        UpdatePrivacyButtonState();

        Loaded += async (s, e) =>
        {
            try
            {
                PositionWindowNearTray();
                await InitializeAsync();
                _ = CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Window_Loaded failed");
            }
        };

        // Track window position changes
        LocationChanged += async (s, e) =>
        {
            try { await SaveWindowPositionAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "LocationChanged handler failed"); }
        };
        SizeChanged += async (s, e) =>
        {
            try { await SaveWindowPositionAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "SizeChanged handler failed"); }
        };
        Activated += (s, e) =>
        {
            _topmostRecoveryGeneration++;
            EnsureAlwaysOnTop();
            LogWindowFocusTransition("Activated");
        };
        Deactivated += (s, e) =>
        {
            if (_isSettingsDialogOpen)
            {
                LogWindowFocusTransition("Deactivated (settings open)");
                return;
            }

            var generation = ++_topmostRecoveryGeneration;
            ScheduleTopmostRecovery(generation, TimeSpan.FromMilliseconds(250));
            ScheduleTopmostRecovery(generation, TimeSpan.FromSeconds(2));
            LogWindowFocusTransition("Deactivated");
        };
        StateChanged += (s, e) =>
        {
            if (WindowState == WindowState.Normal)
            {
                EnsureAlwaysOnTop();
            }

            LogWindowFocusTransition($"StateChanged -> {WindowState}");
        };
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
            {
                EnsureAlwaysOnTop();
            }

            LogWindowFocusTransition($"IsVisibleChanged -> {IsVisible}");
        };
    }

    private void ApplyVersionDisplay()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var appVersion = assembly.GetName().Version;
        var versionCore = appVersion != null
            ? $"{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}"
            : "0.0.0";

        var suffix = GetPrereleaseLabel(assembly);
        var displayVersion = string.IsNullOrWhiteSpace(suffix)
            ? $"v{versionCore}"
            : $"v{versionCore} {suffix}";

        VersionText.Text = displayVersion;
        Title = $"AI Usage Tracker {displayVersion}";
    }

    private static string? GetPrereleaseLabel(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        // Trim build metadata (e.g., +sha) and keep semantic pre-release suffix.
        var normalized = informationalVersion.Split('+')[0];
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex < 0 || dashIndex >= normalized.Length - 1)
        {
            return null;
        }

        var suffix = normalized[(dashIndex + 1)..];
        if (suffix.StartsWith("beta.", StringComparison.OrdinalIgnoreCase))
        {
            var betaPart = suffix["beta.".Length..];
            return string.IsNullOrWhiteSpace(betaPart) ? "Beta" : $"Beta {betaPart}";
        }

        if (suffix.StartsWith("alpha.", StringComparison.OrdinalIgnoreCase))
        {
            var alphaPart = suffix["alpha.".Length..];
            return string.IsNullOrWhiteSpace(alphaPart) ? "Alpha" : $"Alpha {alphaPart}";
        }

        if (suffix.StartsWith("rc.", StringComparison.OrdinalIgnoreCase))
        {
            var rcPart = suffix["rc.".Length..];
            return string.IsNullOrWhiteSpace(rcPart) ? "RC" : $"RC {rcPart}";
        }

        return suffix.Replace('.', ' ');
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_ACTIVATEAPP = 0x001C;

        if (msg == WM_ACTIVATEAPP)
        {
            var isActive = wParam != IntPtr.Zero;
            LogWindowFocusTransition($"WM_ACTIVATEAPP -> {(isActive ? "active" : "inactive")}");
        }

        return IntPtr.Zero;
    }

    private void LogWindowFocusTransition(string eventName)
    {
        var foregroundSummary = GetForegroundWindowSummary();
        var message = $"[WINDOW] evt={eventName} fg={foregroundSummary} vis={IsVisible} state={WindowState} top={Topmost}";
        _logger.LogDebug("{WindowMessage}", message);
    }

    private static string GetForegroundWindowSummary()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return "none";
        }

        var titleLength = GetWindowTextLength(hwnd);
        var builder = new StringBuilder(Math.Max(titleLength + 1, 1));
        _ = GetWindowText(hwnd, builder, builder.Capacity);

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        var processName = "unknown";
        if (processId > 0)
        {
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                processName = "unavailable";
            }
        }

        var title = builder
            .ToString()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            title = "<no-title>";
        }

        if (title.Length > 80)
        {
            title = title[..80] + "...";
        }

        return $"pid={processId} proc={processName} title={title}";
    }

    private void PositionWindowNearTray()
    {
        // If saved position exists, use it
        if (_preferences.WindowLeft.HasValue && _preferences.WindowTop.HasValue)
        {
            // Ensure window is visible on screen
            var screen = SystemParameters.WorkArea;
            var left = Math.Max(screen.Left, Math.Min(_preferences.WindowLeft.Value, screen.Right - Width));
            var top = Math.Max(screen.Top, Math.Min(_preferences.WindowTop.Value, screen.Bottom - Height));

            Left = left;
            Top = top;
        }
    }

    private async Task SaveWindowPositionAsync()
    {
        if (!IsLoaded || !_preferencesLoaded) return;

        // Only save if position has changed meaningfully
        if (Math.Abs(_preferences.WindowLeft.GetValueOrDefault() - Left) > 1 ||
            Math.Abs(_preferences.WindowTop.GetValueOrDefault() - Top) > 1)
        {
            _preferences.WindowLeft = Left;
            _preferences.WindowTop = Top;
            await SaveUiPreferencesAsync();
        }
    }

    private async Task InitializeAsync()
    {
        if (_isLoading || _monitorService == null)
            return;

        try
        {
            _isLoading = true;

            if (!_preferencesLoaded)
            {
                _preferences = await _preferencesStore.LoadAsync();
                App.Preferences = _preferences;
                _isPrivacyMode = _preferences.IsPrivacyMode;
                App.SetPrivacyMode(_isPrivacyMode);
                _preferencesLoaded = true;
                ApplyPreferences();
            }

            ShowStatus("Checking monitor status...", StatusType.Info);

            // Offload the expensive discovery/startup logic to a background thread
            // to prevent UI freezing during port scans or agent startup waits.
            var success = await Task.Run(async () => {
                try {
                    // Full port discovery: check monitor.json, then scan 5000-5010
                    await _monitorService.RefreshPortAsync();
                    
                    // Check if Monitor is running on the discovered port
                    var isRunning = await _monitorService.CheckHealthAsync();
                    
                    if (!isRunning)
                    {
                        Dispatcher.Invoke(() => ShowStatus("Monitor not running. Starting monitor...", StatusType.Warning));

                        if (await MonitorLauncher.StartAgentAsync())
                        {
                            Dispatcher.Invoke(() => ShowStatus("Waiting for monitor...", StatusType.Warning));
                            var monitorReady = await MonitorLauncher.WaitForAgentAsync();

                            if (!monitorReady)
                            {
                                Dispatcher.Invoke(() => {
                                    ShowStatus("Monitor failed to start", StatusType.Error);
                                    ShowErrorState("Monitor failed to start.\n\nPlease ensure AIUsageTracker.Monitor is installed and try again.");
                                });
                                return false;
                            }

                        }
                        else
                        {
                            Dispatcher.Invoke(() => {
                                ShowStatus("Could not start monitor", StatusType.Error);
                                ShowErrorState("Could not start monitor automatically.\n\nPlease start it manually:\n\ndotnet run --project AIUsageTracker.Monitor");
                            });
                            return false;
                        }
                    }

                    // Update monitor toggle button state
                    await UpdateMonitorToggleButtonStateAsync();

                    return true;
                } catch (Exception ex) {
                    _logger.LogError(ex, "Background initialization failed");
                    return false;
                }
            });

            if (success)
            {
                var handshakeResult = await _monitorService.CheckApiContractAsync();
                ApplyMonitorContractStatus(handshakeResult);

                // Rapid polling at startup until data is available
                await RapidPollUntilDataAvailableAsync();

                // Start polling timer - UI polls Agent every minute
                StartPollingTimer();

                ShowStatus("Connected", StatusType.Success);
            }
        }
        catch (Exception ex)
        {
            ShowErrorState($"Initialization failed: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task RapidPollUntilDataAvailableAsync()
    {
        const int maxAttempts = 15;
        const int pollIntervalMs = 2000; // 2 seconds between attempts

        LogDiagnostic("[DIAGNOSTIC] RapidPollUntilDataAvailableAsync starting...");
        ShowStatus("Loading data...", StatusType.Info);

        // First, check if Monitor is reachable
        LogDiagnostic("[DIAGNOSTIC] Checking Monitor health...");
        var isHealthy = await _monitorService.CheckHealthAsync();
        LogDiagnostic($"[DIAGNOSTIC] Monitor health: {isHealthy}");
        
        if (!isHealthy)
        {
            ShowStatus("Monitor not reachable", StatusType.Error);
            ShowErrorState("Cannot connect to Monitor.\n\nPlease ensure:\n1. Monitor is running\n2. Port is correct (check monitor.json)\n3. Firewall is not blocking\n\nTry restarting the Monitor.");
            return;
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            LogDiagnostic($"[DIAGNOSTIC] Poll attempt {attempt + 1}/{maxAttempts}");
            
            try
            {
                // Try to get cached data from monitor
                LogDiagnostic("[DIAGNOSTIC] Calling GetUsageAsync...");
                var usages = await _monitorService.GetUsageAsync();
                LogDiagnostic($"[DIAGNOSTIC] GetUsageAsync returned {usages.Count} providers");

                // Show all providers from monitor (filtering already done in database)
                if (usages.Any())
                {
                    LogDiagnostic("[DIAGNOSTIC] Data available, rendering...");
                    // Data is available - render and stop rapid polling
                    _usages = usages.ToList();
                    RenderProviders();
                    _lastMonitorUpdate = DateTime.Now;
                    ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                    _ = UpdateTrayIconsAsync();
                    LogDiagnostic("[DIAGNOSTIC] Data rendered successfully");
                    return;
                }

                LogDiagnostic("[DIAGNOSTIC] No data available");
                
                // No data yet - on first attempt, trigger a background refresh
                // and keep polling so data appears as soon as refresh completes.
                if (attempt == 0)
                {
                    LogDiagnostic("[DIAGNOSTIC] First attempt, no data - triggering background refresh...");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _monitorService.TriggerRefreshAsync();
                            LogDiagnostic("[DIAGNOSTIC] Background refresh triggered");
                        }
                        catch (Exception ex)
                        {
                            LogDiagnostic($"[DIAGNOSTIC] Background refresh failed: {ex.Message}");
                        }
                    });
                    
                    // Show UI immediately with empty state
                    LogDiagnostic("[DIAGNOSTIC] Showing empty state...");
                    ShowStatus("Scanning for providers...", StatusType.Info);
                    LogDiagnostic("[DIAGNOSTIC] About to call RenderProviders...");
                    RenderProviders(); // Will show empty or loading state
                    LogDiagnostic("[DIAGNOSTIC] RenderProviders completed");
                }

                // No data yet - wait and try again
                if (attempt < maxAttempts - 1)
                {
                    ShowStatus($"Waiting for data... ({attempt + 1}/{maxAttempts})", StatusType.Warning);
                    await Task.Delay(pollIntervalMs);
                }
            }
            catch (HttpRequestException ex)
            {
                LogDiagnostic($"[DIAGNOSTIC] Connection error: {ex.Message}");
                ShowStatus("Connection lost", StatusType.Error);
                ShowErrorState($"Lost connection to Monitor:\n{ex.Message}\n\nTry refreshing or restarting the Monitor.");
                return;
            }
            catch (Exception ex)
            {
                LogDiagnostic($"[DIAGNOSTIC] Error: {ex.Message}");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(pollIntervalMs);
                }
            }
        }
        
        LogDiagnostic("[DIAGNOSTIC] Max attempts reached, no data available");
        ShowStatus("No data available", StatusType.Error);
        ShowErrorState("No provider data available.\n\nThe Monitor may still be initializing.\nTry refreshing manually or check Settings > Monitor.");
    }

    private void ApplyPreferences()
    {
        // Apply window settings
        this.Topmost = _preferences.AlwaysOnTop;
        this.Width = _preferences.WindowWidth;
        this.Height = _preferences.WindowHeight;
        // Note: PositionWindowNearTray() is only called on initial load, not here
        // to prevent window from jumping when closing settings dialog

        if (!string.IsNullOrWhiteSpace(_preferences.FontFamily))
        {
            this.FontFamily = new FontFamily(_preferences.FontFamily);
        }

        if (_preferences.FontSize > 0)
        {
            this.FontSize = _preferences.FontSize;
        }

        this.FontWeight = _preferences.FontBold ? FontWeights.Bold : FontWeights.Normal;
        this.FontStyle = _preferences.FontItalic ? FontStyles.Italic : FontStyles.Normal;

        // Apply UI controls
        AlwaysOnTopCheck.IsChecked = _preferences.AlwaysOnTop;
        ShowUsedToggle.IsChecked = _preferences.InvertProgressBar;
        UpdatePrivacyButtonState();
        EnsureAlwaysOnTop();
        
        // Reinitialize update checker with correct channel
        InitializeUpdateChecker();
    }
    
    private void InitializeUpdateChecker()
    {
        if (_preferences == null) return;
        
        var channel = _preferences.UpdateChannel;
        _updateChecker = new GitHubUpdateChecker(NullLogger<GitHubUpdateChecker>.Instance, channel);
    }

    private async Task SaveUiPreferencesAsync()
    {
        App.Preferences = _preferences;
        var saved = await _preferencesStore.SaveAsync(_preferences);
        if (!saved)
        {
            _logger.LogWarning("Failed to save Slim UI preferences");
        }
    }

    private void EnsureAlwaysOnTop()
    {
        if (_isSettingsDialogOpen || _isTooltipOpen || !_preferences.AlwaysOnTop || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        if (!Topmost)
        {
            Topmost = true;
        }

        ApplyWin32Topmost(noActivate: true);
    }

    private void ApplyTopmostState(bool alwaysOnTop)
    {
        Topmost = alwaysOnTop;

        if (_preferences.ForceWin32Topmost)
        {
            ApplyWin32Topmost(noActivate: true, alwaysOnTop);
        }
    }

    private void ScheduleTopmostRecovery(int generation, TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != _topmostRecoveryGeneration)
                {
                    return;
                }

                ReassertTopmostWithoutFocus();
                LogWindowFocusTransition($"TopmostRecovery +{delay.TotalMilliseconds:0}ms");
            }, DispatcherPriority.Normal);
        });
    }

    private void ReassertTopmostWithoutFocus()
    {
        if (_isSettingsDialogOpen || _isTooltipOpen || !_preferences.AlwaysOnTop || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        if (!Topmost)
        {
            Topmost = true;
            ApplyWin32Topmost(noActivate: true);
            return;
        }

        if (_preferences.AggressiveAlwaysOnTop)
        {
            Topmost = false;
            Topmost = true;
        }

        ApplyWin32Topmost(noActivate: true);
    }

    private void ApplyWin32Topmost(bool noActivate, bool alwaysOnTop = true)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var flags = SwpNoMove | SwpNoSize | SwpNoOwnerZOrder;
        if (noActivate)
        {
            flags |= SwpNoActivate;
        }

        var insertAfter = alwaysOnTop ? HwndTopmost : HwndNoTopmost;
        var applied = SetWindowPos(handle, insertAfter, 0, 0, 0, 0, flags);
        if (!applied)
        {
            var win32Error = Marshal.GetLastWin32Error();
            _logger.LogWarning(
                "SetWindowPos failed err={Win32Error} alwaysOnTop={AlwaysOnTop} noActivate={NoActivate}",
                win32Error,
                alwaysOnTop,
                noActivate);
        }
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        EnsureAlwaysOnTop();
    }

    internal async Task PrepareForHeadlessScreenshotAsync(bool deterministic = false)
    {
        if (deterministic)
        {
            var fixture = MainWindowDeterministicFixture.Create();
            _preferences = fixture.Preferences;

            App.Preferences = _preferences;
            _isPrivacyMode = true;
            App.SetPrivacyMode(true);
            _preferencesLoaded = true;
            _lastMonitorUpdate = fixture.LastMonitorUpdate;
            ApplyPreferences();
            Width = fixture.WindowWidth;
            Height = MinHeight;
            _usages = fixture.Usages;

            RenderProviders();
            ShowStatus($"{_lastMonitorUpdate:HH:mm:ss}", StatusType.Success);
        }
        else
        {
            await InitializeAsync();
        }
    }

    private void FitWindowHeightForHeadlessScreenshot()
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        var width = Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = ActualWidth > 0 ? ActualWidth : 460;
        }

        root.Measure(new Size(width, double.PositiveInfinity));
        var desiredHeight = Math.Ceiling(root.DesiredSize.Height);
        if (desiredHeight > 0)
        {
            Height = Math.Max(MinHeight, desiredHeight);
        }

        UpdateLayout();

        if (ProvidersScrollViewer is null)
        {
            return;
        }

        var overflow = ProvidersScrollViewer.ExtentHeight - ProvidersScrollViewer.ViewportHeight;
        if (overflow > 0.5)
        {
            Height += Math.Ceiling(overflow) + 2;
            UpdateLayout();
        }
    }

    private void OnPrivacyChanged(object? sender, bool isPrivacyMode)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnPrivacyChanged(sender, isPrivacyMode));
            return;
        }

        _isPrivacyMode = isPrivacyMode;
        _preferences.IsPrivacyMode = isPrivacyMode;
        UpdatePrivacyButtonState();

        if (_usages.Count > 0)
        {
            RenderProviders();
        }
    }

    private void UpdatePrivacyButtonState()
    {
        if (PrivacyBtn == null)
        {
            return;
        }

        PrivacyBtn.Content = _isPrivacyMode ? "\uE72E" : "\uE785";
        PrivacyBtn.Foreground = _isPrivacyMode
            ? Brushes.Gold
            : (TryFindResource("SecondaryText") as Brush ?? Brushes.Gray);
    }

    private async Task RefreshDataAsync()
    {
        if (_isLoading || _monitorService == null)
            return;

        try
        {
            _isLoading = true;
            ShowStatus("Refreshing...", StatusType.Info);

            // Trigger refresh on monitor
            await _monitorService.TriggerRefreshAsync();

            // Get updated usage data
            var latestUsages = await _monitorService.GetUsageAsync();
            if (latestUsages.Any())
            {
                _usages = latestUsages.ToList();
                RenderProviders();
                _lastMonitorUpdate = DateTime.Now;
                ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                _ = UpdateTrayIconsAsync();
                return;
            }

            if (_usages.Any())
            {
                ShowStatus("Refresh returned no data, keeping last snapshot", StatusType.Warning);
                return;
            }

            ShowErrorState("No provider data available.\n\nMonitor may still be initializing.");
        }
        catch (Exception ex)
        {
            ShowErrorState($"Refresh failed: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    // UI Element Creation Helpers
    private static TextBlock CreateText(string text, double fontSize, Brush foreground,
        FontWeight? fontWeight = null, Thickness? margin = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground,
            FontWeight = fontWeight ?? FontWeights.Normal,
            Margin = margin ?? new Thickness(0)
        };
    }

    private static Border CreateSeparator(Brush color, double opacity = 0.5, double height = 1)
    {
        return new Border
        {
            Height = height,
            Background = color,
            Opacity = opacity,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Grid CreateCollapsibleHeaderGrid(Thickness margin)
    {
        var header = new Grid { Margin = margin };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return header;
    }

    private SolidColorBrush GetResourceBrush(string key, SolidColorBrush fallback)
    {
        return FindResource(key) as SolidColorBrush ?? fallback;
    }

    private void RenderProviders()
    {
        LogDiagnostic("[DIAGNOSTIC] RenderProviders called");
        ProvidersList.Children.Clear();
        LogDiagnostic($"[DIAGNOSTIC] ProvidersList cleared, _usages count: {_usages?.Count ?? 0}");

        if (_usages == null || !_usages.Any())
        {
            LogDiagnostic("[DIAGNOSTIC] No usages, creating 'No provider data available' message");
            try
            {
                var messageBlock = CreateInfoTextBlock("No provider data available.");
                ProvidersList.Children.Add(messageBlock);
                ApplyProviderListFontPreferences();
                LogDiagnostic("[DIAGNOSTIC] 'No provider data available' message added");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add empty-state provider message");
            }
            return;
        }

        try
        {
            LogDiagnostic($"[DIAGNOSTIC] Rendering {_usages.Count} providers...");

            var renderPreparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(_usages);
            var filteredUsages = renderPreparation.DisplayableUsages;

            LogDiagnostic(
                $"[DIAGNOSTIC] Provider render counts: raw={_usages.Count}, filtered={filteredUsages.Count}, hasAntigravityParent={renderPreparation.HasAntigravityParent}");

            if (!filteredUsages.Any())
            {
                ProvidersList.Children.Add(CreateInfoTextBlock("Data received, but no displayable providers were found."));
                ApplyProviderListFontPreferences();
                return;
            }

            // Render Quota Providers first, then PAYG
            var orderedUsages = filteredUsages
                .OrderByDescending(u => u.IsQuotaBased)
                .ThenBy(u => ProviderMetadataCatalog.GetDisplayName(u.ProviderId ?? string.Empty, u.ProviderName), StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.ProviderId, StringComparer.OrdinalIgnoreCase);

            UIElement? currentHeader = null;
            StackPanel? currentContainer = null;
            bool? currentIsQuota = null;

            foreach (var usage in orderedUsages)
            {
                // Switch section if type changes
                if (currentIsQuota != usage.IsQuotaBased)
                {
                    currentIsQuota = usage.IsQuotaBased;
                    var sectionTitle = currentIsQuota.Value ? "Plans & Quotas" : "Pay As You Go";
                    var sectionColor = currentIsQuota.Value ? Brushes.DeepSkyBlue : Brushes.MediumSeaGreen;
                    var sectionKey = currentIsQuota.Value ? "PlansAndQuotas" : "PayAsYouGo";
                    
                    var (header, container) = CreateCollapsibleHeader(
                        sectionTitle,
                        sectionColor,
                        isGroupHeader: true,
                        groupKey: sectionKey,
                        () => currentIsQuota.Value ? _preferences.IsPlansAndQuotasCollapsed : _preferences.IsPayAsYouGoCollapsed,
                        v => {
                            if (currentIsQuota.Value) _preferences.IsPlansAndQuotasCollapsed = v;
                            else _preferences.IsPayAsYouGoCollapsed = v;
                        });

                    ProvidersList.Children.Add(header);
                    ProvidersList.Children.Add(container);
                    currentHeader = header;
                    currentContainer = container;
                }

                if (currentContainer == null) continue;

                // Check if this section is collapsed
                var isCollapsed = currentIsQuota.Value ? _preferences.IsPlansAndQuotasCollapsed : _preferences.IsPayAsYouGoCollapsed;
                if (isCollapsed) continue;

                // Special handling for Antigravity
                if (ProviderMetadataCatalog.IsAggregateParentProviderId(usage.ProviderId ?? string.Empty))
                {
                    if (usage.Details?.Any() == true)
                    {
                        AddAntigravityModels(usage, currentContainer);
                    }
                    else
                    {
                        AddAntigravityUnavailableNotice(usage, currentContainer);
                    }
                    continue;
                }

                // Standard provider card
                AddProviderCard(usage, currentContainer);

                // Sub-providers if available
                if (usage.Details?.Any() == true)
                {
                    AddCollapsibleSubProviders(usage, currentContainer);
                }
            }

            ApplyProviderListFontPreferences();
        }
        catch (Exception ex)
        {
            LogDiagnostic($"[DIAGNOSTIC] RenderProviders failed: {ex}");
            ProvidersList.Children.Clear();
            ProvidersList.Children.Add(CreateInfoTextBlock("Failed to render provider cards. Check logs for details."));
            ApplyProviderListFontPreferences();
        }
    }

    private void ApplyProviderListFontPreferences()
    {
        if (ProvidersList == null)
        {
            return;
        }

        ApplyFontPreferencesToElement(ProvidersList);
    }

    private void ApplyFontPreferencesToElement(DependencyObject element)
    {
        if (element is TextBlock textBlock)
        {
            if (!string.IsNullOrWhiteSpace(_preferences.FontFamily))
            {
                textBlock.FontFamily = new FontFamily(_preferences.FontFamily);
            }

            if (_preferences.FontSize > 0)
            {
                textBlock.FontSize = Math.Max(8, textBlock.FontSize * (_preferences.FontSize / 12.0));
            }

            if (_preferences.FontBold)
            {
                textBlock.FontWeight = FontWeights.Bold;
            }

            if (_preferences.FontItalic)
            {
                textBlock.FontStyle = FontStyles.Italic;
            }
        }

        switch (element)
        {
            case Panel panel:
                foreach (UIElement child in panel.Children)
                {
                    ApplyFontPreferencesToElement(child);
                }
                break;

            case Border border when border.Child is not null:
                ApplyFontPreferencesToElement(border.Child);
                break;

            case Decorator decorator when decorator.Child is not null:
                ApplyFontPreferencesToElement(decorator.Child);
                break;

            case ContentControl contentControl when contentControl.Content is DependencyObject child:
                ApplyFontPreferencesToElement(child);
                break;
        }
    }

    private (UIElement Header, StackPanel Container) CreateCollapsibleHeader(
        string title, Brush accent, bool isGroupHeader, string? groupKey,
        Func<bool> getCollapsed, Action<bool> setCollapsed)
    {
        // Group header has larger margins, sub-header is indented
        var margin = isGroupHeader
            ? new Thickness(0, 8, 0, 4)
            : new Thickness(20, 4, 0, 2);
        var fontSize = isGroupHeader ? 10.0 : 9.0;
        var titleFontWeight = isGroupHeader ? FontWeights.Bold : FontWeights.Normal;
        var toggleOpacity = isGroupHeader ? 1.0 : 0.8;
        var lineOpacity = isGroupHeader ? 0.5 : 0.3;
        var titleText = isGroupHeader ? title.ToUpper(System.Globalization.CultureInfo.InvariantCulture) : title;
        var titleForeground = isGroupHeader ? accent : GetResourceBrush("SecondaryText", Brushes.Gray);

        var header = CreateCollapsibleHeaderGrid(margin);

        // Toggle button
        var toggleText = CreateText(
            getCollapsed() ? "▶" : "▼",
            fontSize,
            accent,
            FontWeights.Bold,
            new Thickness(0, 0, 5, 0));
        toggleText.VerticalAlignment = VerticalAlignment.Center;
        toggleText.Opacity = toggleOpacity;
        toggleText.Tag = "ToggleIcon";

        // Title
        var titleBlock = CreateText(
            titleText,
            10.0,
            titleForeground,
            titleFontWeight,
            new Thickness(0, 0, 10, 0));
        titleBlock.VerticalAlignment = VerticalAlignment.Center;

        // Separator line
        var line = CreateSeparator(accent, lineOpacity);

        // Container
        var container = new StackPanel();
        if (!string.IsNullOrEmpty(groupKey))
            container.Tag = $"{groupKey}Container";
        container.Visibility = getCollapsed() ? Visibility.Collapsed : Visibility.Visible;

        // Click handler
        header.Cursor = System.Windows.Input.Cursors.Hand;
        header.MouseLeftButtonDown += async (s, e) =>
        {
            var newState = !getCollapsed();
            setCollapsed(newState);
            container.Visibility = newState ? Visibility.Collapsed : Visibility.Visible;
            toggleText.Text = newState ? "▶" : "▼";
            await SaveUiPreferencesAsync();
        };

        Grid.SetColumn(toggleText, 0);
        Grid.SetColumn(titleBlock, 1);
        Grid.SetColumn(line, 2);

        header.Children.Add(toggleText);
        header.Children.Add(titleBlock);
        header.Children.Add(line);

        return (header, container);
    }

    private void AddProviderCard(ProviderUsage usage, StackPanel container, bool isChild = false)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        var friendlyName = ProviderMetadataCatalog.GetDisplayName(providerId, usage.ProviderName);
        var showUsed = ShowUsedToggle?.IsChecked ?? false;
        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed);

        // Main Grid Container - single row layout
        var grid = new Grid
        {
            Margin = new Thickness(isChild ? 20 : 0, 0, 0, 2),
            Height = 24,
            Background = Brushes.Transparent,
            Tag = providerId
        };

        // Background Progress Bar
        var pGrid = new Grid();

        if (ProviderDualWindowPresentationCatalog.TryGetDualWindowUsedPercentages(usage, out var hourlyUsed, out var weeklyUsed))
        {
            pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var hourlyRow = CreateProgressLayer(hourlyUsed, showUsed, opacity: 0.55);
            var weeklyRow = CreateProgressLayer(weeklyUsed, showUsed, opacity: 0.35);
            Grid.SetRow(hourlyRow, 0);
            Grid.SetRow(weeklyRow, 1);
            pGrid.Children.Add(hourlyRow);
            pGrid.Children.Add(weeklyRow);
        }
        else
        {
            var indicatorWidth = showUsed ? presentation.UsedPercent : presentation.RemainingPercent;
            pGrid = CreateSingleProgressLayer(presentation.UsedPercent, indicatorWidth, opacity: 0.45);
        }

        pGrid.Visibility = presentation.ShouldHaveProgress ? Visibility.Visible : Visibility.Collapsed;
        grid.Children.Add(pGrid);

        // Background for non-progress items
        var bg = new Border
        {
            Background = GetResourceBrush("CardBackground", Brushes.DarkGray),
            CornerRadius = new CornerRadius(0),
            Visibility = presentation.ShouldHaveProgress ? Visibility.Collapsed : Visibility.Visible
        };
        grid.Children.Add(bg);

        // Content Overlay
        var contentPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 6, 0) };

        // Provider icon or bullet for child items
        if (isChild)
        {
            AddDockedElement(contentPanel, CreateBulletMarker(), Dock.Left);
        }
        else
        {
            // Provider icon for parent items
            var providerIcon = CreateProviderIcon(providerId);
            providerIcon.Margin = new Thickness(0, 0, 6, 0); // Reduced margin for specific alignment
            providerIcon.Width = 14;
            providerIcon.Height = 14;
            providerIcon.VerticalAlignment = VerticalAlignment.Center;
            AddDockedElement(contentPanel, providerIcon, Dock.Left);
        }

        // Right Side: Usage/Status
        var statusText = presentation.StatusText;
        Brush statusBrush = presentation.StatusTone switch
        {
            ProviderCardStatusTone.Missing => Brushes.IndianRed,
            ProviderCardStatusTone.Warning => Brushes.Orange,
            ProviderCardStatusTone.Error => Brushes.Red,
            _ => GetResourceBrush("SecondaryText", Brushes.Gray)
        };

        // Reset time display (if available) - shown with muted golden color
        if (!presentation.SuppressSingleResetTime && usage.NextResetTime.HasValue)
        {
            var relative = GetRelativeTimeString(usage.NextResetTime.Value);
            AddDockedElement(
                contentPanel,
                CreateDockedTextBlock(
                    $"(Resets: {relative})",
                    fontSize: 10,
                    foreground: GetResourceBrush("StatusTextWarning", Brushes.Goldenrod),
                    fontWeight: FontWeights.SemiBold,
                    margin: new Thickness(10, 0, 0, 0)),
                Dock.Right);
        }

        // Right Side: Usage/Status - must be added last to Dock.Right to appear left of reset time
        AddDockedElement(
            contentPanel,
            CreateDockedTextBlock(
                statusText,
                fontSize: 10,
                foreground: statusBrush,
                margin: new Thickness(10, 0, 0, 0)),
            Dock.Right);

        // Name (gets remaining space)
        var accountPart = string.IsNullOrWhiteSpace(usage.AccountName)
            ? ""
            : $" [{(_isPrivacyMode ? ProviderStatusPresentationCatalog.MaskAccountIdentifier(usage.AccountName) : usage.AccountName)}]";
        AddDockedElement(
            contentPanel,
            CreateDockedTextBlock(
                $"{friendlyName}{accountPart}",
                fontSize: 11,
                foreground: presentation.IsMissing ? GetResourceBrush("TertiaryText", Brushes.Gray) : GetResourceBrush("PrimaryText", Brushes.White),
                fontWeight: isChild ? FontWeights.Normal : FontWeights.SemiBold,
                textTrimming: TextTrimming.CharacterEllipsis),
            Dock.Left);

        grid.Children.Add(contentPanel);

        var toolTipContent = ProviderTooltipPresentationCatalog.BuildContent(usage, friendlyName);
        if (!string.IsNullOrEmpty(toolTipContent))
        {
            grid.ToolTip = CreateTopmostAwareToolTip(grid, toolTipContent);
            ConfigureCardToolTip(grid);
        }

        container.Children.Add(grid);
    }

    private void AddAntigravityModels(ProviderUsage usage, StackPanel container)
    {
        foreach (var modelUsage in ProviderUsageDisplayCatalog.CreateAntigravityModelUsages(usage))
        {
            AddProviderCard(modelUsage, container);
        }
    }

    private void AddAntigravityUnavailableNotice(ProviderUsage usage, StackPanel container)
    {
        var reason = string.IsNullOrWhiteSpace(usage.Description)
            ? "Model quota details are missing from the latest monitor refresh."
            : usage.Description;

        var message =
            "Antigravity model quotas unavailable. " +
            $"{reason} Use Refresh to request live data from Antigravity.";

        container.Children.Add(CreateInfoTextBlock(message));
    }

    private ToolTip CreateTopmostAwareToolTip(FrameworkElement placementTarget, object content)
    {
        var toolTip = new ToolTip
        {
            Content = content,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            PlacementTarget = placementTarget
        };

        toolTip.Opened += (s, e) =>
        {
            _isTooltipOpen = true;
            if (s is ToolTip tip && tip.PlacementTarget != null)
            {
                var tooltipWindow = Window.GetWindow(tip);
                if (tooltipWindow != null && this.Topmost)
                {
                    tooltipWindow.Topmost = true;
                }
            }
        };
        toolTip.Closed += (s, e) => _isTooltipOpen = false;

        return toolTip;
    }

    private static void AddDockedElement(DockPanel panel, UIElement element, Dock dock)
    {
        panel.Children.Add(element);
        DockPanel.SetDock(element, dock);
    }

    private TextBlock CreateDockedTextBlock(
        string text,
        double fontSize,
        Brush foreground,
        FontWeight? fontWeight = null,
        Thickness? margin = null,
        TextTrimming textTrimming = TextTrimming.None)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground,
            FontWeight = fontWeight ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin ?? new Thickness(0),
            TextTrimming = textTrimming
        };
    }

    private Border CreateBulletMarker()
    {
        return new Border
        {
            Width = 4,
            Height = 4,
            Background = GetResourceBrush("SecondaryText", Brushes.Gray),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(2, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static void ConfigureCardToolTip(FrameworkElement target)
    {
        ToolTipService.SetInitialShowDelay(target, 100);
        ToolTipService.SetShowDuration(target, 15000);
    }

    private Grid CreateProgressLayer(double usedPercent, bool showUsed, double opacity)
    {
        var remainingPercent = Math.Max(0, 100 - usedPercent);
        var indicatorWidth = showUsed ? usedPercent : remainingPercent;
        return CreateSingleProgressLayer(usedPercent, indicatorWidth, opacity);
    }

    private Grid CreateSingleProgressLayer(double usedPercent, double indicatorWidth, double opacity)
    {
        var clampedWidth = Math.Clamp(indicatorWidth, 0, 100);

        var layer = new Grid();
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clampedWidth, GridUnitType.Star) });
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - clampedWidth), GridUnitType.Star) });

        layer.Children.Add(new Border
        {
            Background = GetProgressBarColor(usedPercent),
            Opacity = opacity,
            CornerRadius = new CornerRadius(0)
        });

        return layer;
    }

    private void AddSubProviderCard(ProviderUsage usage, ProviderUsageDetail detail, StackPanel container)
    {
        // Compact sub-item (child provider detail)
        var grid = new Grid
        {
            Margin = new Thickness(20, 0, 0, 2),
            Height = 20,
            Background = Brushes.Transparent
        };

        var presentation = ProviderSubDetailPresentationCatalog.Create(
            detail,
            usage.IsQuotaBased,
            ShowUsedToggle?.IsChecked ?? false,
            GetRelativeTimeString);

        // Background Progress Bar (Miniature)
        var pGrid = CreateSingleProgressLayer(presentation.UsedPercent, presentation.IndicatorWidth, opacity: 0.3);
        if (presentation.HasProgress)
        {
            grid.Children.Add(pGrid);
        }

        // Content Overlay
        var bulletPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 6, 0) };

        AddDockedElement(bulletPanel, CreateBulletMarker(), Dock.Left);

        // Reset time on the right (if available) - shown in yellow
        if (!string.IsNullOrEmpty(presentation.ResetText))
        {
            AddDockedElement(
                bulletPanel,
                CreateDockedTextBlock(
                    presentation.ResetText,
                    fontSize: 9,
                    foreground: GetResourceBrush("StatusTextWarning", Brushes.Goldenrod),
                    fontWeight: FontWeights.SemiBold,
                    margin: new Thickness(6, 0, 0, 0)),
                Dock.Right);
        }

        // Value on the right
        AddDockedElement(
            bulletPanel,
            CreateDockedTextBlock(
                presentation.DisplayText,
                fontSize: 10,
                foreground: GetResourceBrush("TertiaryText", Brushes.Gray),
                margin: new Thickness(10, 0, 0, 0)),
            Dock.Right);

        // Name on the left
        AddDockedElement(
            bulletPanel,
            CreateDockedTextBlock(
                detail.Name,
                fontSize: 10,
                foreground: GetResourceBrush("SecondaryText", Brushes.LightGray),
                textTrimming: TextTrimming.CharacterEllipsis),
            Dock.Left);

        grid.Children.Add(bulletPanel);
        container.Children.Add(grid);
    }

    private void AddCollapsibleSubProviders(ProviderUsage usage, StackPanel container)
    {
        if (usage.Details?.Any() != true) return;

        var displayableDetails = ProviderSubDetailPresentationCatalog.GetDisplayableDetails(usage);

        if (!displayableDetails.Any())
        {
            return;
        }

        // Create collapsible section for sub-providers
        var useAntigravityCollapsePreference = ProviderMetadataCatalog.IsAggregateParentProviderId(
            ProviderMetadataCatalog.GetCanonicalProviderId(usage.ProviderId ?? string.Empty));
        var (subHeader, subContainer) = CreateCollapsibleHeader(
            $"{usage.ProviderName} Details",
            Brushes.DeepSkyBlue,
            isGroupHeader: false,
            groupKey: null,
            () => useAntigravityCollapsePreference && _preferences.IsAntigravityCollapsed,
            v =>
            {
                if (useAntigravityCollapsePreference)
                {
                    _preferences.IsAntigravityCollapsed = v;
                }
            });

        container.Children.Add(subHeader);
        container.Children.Add(subContainer);

        if (!useAntigravityCollapsePreference || !_preferences.IsAntigravityCollapsed)
        {
            // Add sub-provider details
            foreach (var detail in displayableDetails)
            {
                AddSubProviderCard(usage, detail, subContainer);
            }
        }
    }

    private string GetRelativeTimeString(DateTime nextReset)
    {
        var diff = nextReset - DateTime.Now;

        if (diff.TotalSeconds <= 0) return "0m";
        if (diff.TotalDays >= 1) return $"{diff.Days}d {diff.Hours}h";
        if (diff.TotalHours >= 1) return $"{diff.Hours}h {diff.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes))}m";
    }

    private FrameworkElement CreateProviderIcon(string providerId)
    {
        var normalizedProviderId = ProviderVisualCatalog.GetCanonicalProviderId(providerId);

        // Check cache first
        if (_iconCache.TryGetValue(normalizedProviderId, out var cachedImage))
        {
            return new Image
            {
                Source = cachedImage,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // Map provider IDs to filename
        var filename = ProviderVisualCatalog.GetIconAssetName(providerId);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var svgPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.svg");

        if (System.IO.File.Exists(svgPath))
        {
            try
            {
                var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings
                {
                    IncludeRuntime = true,
                    TextAsGeometry = true
                };
                var reader = new SharpVectors.Converters.FileSvgReader(settings);
                var drawing = reader.Read(svgPath);
                if (drawing != null)
                {
                    var image = new DrawingImage(drawing);
                    image.Freeze();
                    _iconCache[normalizedProviderId] = image;

                    return new Image
                    {
                        Source = image,
                        Width = 16,
                        Height = 16,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            catch
            {
                // Fallback to circle with initial
            }
        }

        // Fallback: colored circle with initial
        return CreateFallbackIcon(normalizedProviderId);
    }

    private FrameworkElement CreateFallbackIcon(string providerId)
    {
        var (color, initial) = ProviderVisualCatalog.GetFallbackBadge(
            providerId,
            GetResourceBrush("SecondaryText", Brushes.Gray));

        var grid = new Grid { Width = 16, Height = 16 };

        var circle = new Border
        {
            Width = 16,
            Height = 16,
            Background = color,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        grid.Children.Add(circle);

        var text = new TextBlock
        {
            Text = initial,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        grid.Children.Add(text);

        return grid;
    }

    private Brush GetProgressBarColor(double usedPercentage)
    {
        var yellowThreshold = _preferences.ColorThresholdYellow;
        var redThreshold = _preferences.ColorThresholdRed;

        if (usedPercentage >= redThreshold) return GetResourceBrush("ProgressBarRed", Brushes.Crimson);
        if (usedPercentage >= yellowThreshold) return GetResourceBrush("ProgressBarYellow", Brushes.Gold);
        return GetResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen);
    }

    private void StartPollingTimer()
    {
        _pollingTimer?.Stop();

        _pollingTimer = new DispatcherTimer
        {
            Interval = _usages.Any() ? NormalPollingInterval : StartupPollingInterval
        };

        _pollingTimer.Tick += async (s, e) =>
        {
            if (_isPollingInProgress)
            {
                return;
            }

            _isPollingInProgress = true;
            // Poll monitor every minute for fresh data
            try
            {
                var usages = await _monitorService.GetUsageAsync();
                
                // Show all providers from monitor (filtering already done in database)
                if (usages.Any())
                {
                    // Fresh data received - update UI
                    _usages = usages.ToList();
                    RenderProviders();
                    _lastMonitorUpdate = DateTime.Now;
                    ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                    _ = UpdateTrayIconsAsync();
                    if (_pollingTimer != null && _pollingTimer.Interval != NormalPollingInterval)
                    {
                        _pollingTimer.Interval = NormalPollingInterval;
                    }
                }
                else
                {
                    // Empty data - try to trigger a refresh if cooldown has passed
                    // This handles cases where Monitor restarted or hasn't completed its background refresh
                    var secondsSinceLastRefresh = (DateTime.Now - _lastRefreshTrigger).TotalSeconds;
                    if (secondsSinceLastRefresh >= RefreshCooldownSeconds)
                    {
                        _logger.LogDebug("Polling returned empty, triggering refresh");
                        _lastRefreshTrigger = DateTime.Now;
                        try
                        {
                            await _monitorService.TriggerRefreshAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "TriggerRefreshAsync failed during polling retry");
                        }
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Polling returned empty, refresh cooldown active ({SecondsSinceLastRefresh:F0}s ago)",
                            secondsSinceLastRefresh);
                    }
                    
                    // Wait a moment and retry getting data
                    await Task.Delay(1000);
                    var retryUsages = await _monitorService.GetUsageAsync();
                    
                    if (retryUsages.Any())
                    {
                        _usages = retryUsages.ToList();
                        RenderProviders();
                        _lastMonitorUpdate = DateTime.Now;
                        ShowStatus($"{DateTime.Now:HH:mm:ss} (refreshed)", StatusType.Success);
                        _ = UpdateTrayIconsAsync();
                        if (_pollingTimer != null && _pollingTimer.Interval != NormalPollingInterval)
                        {
                            _pollingTimer.Interval = NormalPollingInterval;
                        }
                    }
                    else if (_usages.Any())
                    {
                        // Keep showing old data, show yellow warning
                        ShowStatus("Last update: " + _lastMonitorUpdate.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " (stale)", StatusType.Warning);
                    }
                    else
                    {
                        // No current data and no previous data - show warning
                        ShowStatus("No data - waiting for Monitor", StatusType.Warning);
                        if (_pollingTimer != null && _pollingTimer.Interval != StartupPollingInterval)
                        {
                            _pollingTimer.Interval = StartupPollingInterval;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Polling loop error");
                if (_usages.Any())
                {
                    // Has old data - show yellow warning, keep displaying stale data
                    ShowStatus("Connection lost - showing stale data", StatusType.Warning);
                }
                else
                {
                    // No old data - show red error
                    ShowStatus("Connection error", StatusType.Error);
                    if (_pollingTimer != null && _pollingTimer.Interval != StartupPollingInterval)
                    {
                        _pollingTimer.Interval = StartupPollingInterval;
                    }
                }
            }
            finally
            {
                _isPollingInProgress = false;
            }
        };

        _pollingTimer.Start();
    }

    private async Task UpdateTrayIconsAsync()
    {
        if (Application.Current is not App app)
        {
            return;
        }

        if (_isTrayIconUpdateInProgress)
        {
            return;
        }

        _isTrayIconUpdateInProgress = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var shouldRefreshConfigs = !_configs.Any() ||
                (DateTime.UtcNow - _lastTrayConfigRefresh) >= TrayConfigRefreshInterval;

            if (shouldRefreshConfigs)
            {
                _configs = (await _monitorService.GetConfigsAsync().ConfigureAwait(true)).ToList();
                _lastTrayConfigRefresh = DateTime.UtcNow;
            }

            app.UpdateProviderTrayIcons(_usages, _configs, _preferences);
        }
        catch (Exception ex)
        {
            LogDiagnostic($"[DIAGNOSTIC] UpdateTrayIconsAsync failed: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            LogDiagnostic($"[DIAGNOSTIC] UpdateTrayIconsAsync completed in {stopwatch.ElapsedMilliseconds}ms");
            _isTrayIconUpdateInProgress = false;
        }
    }

    private void LogDiagnostic(string message)
    {
        _logger.LogInformation("{DiagnosticMessage}", message);
    }

    private void ShowStatus(string message, StatusType type)
    {
        if (type == StatusType.Success && !string.IsNullOrWhiteSpace(_monitorContractWarningMessage))
        {
            message = _monitorContractWarningMessage;
            type = StatusType.Warning;
        }

        if (StatusText != null)
        {
            StatusText.Text = message;
        }

        // Update LED color
        if (StatusLed != null)
        {
            StatusLed.Fill = type switch
            {
                StatusType.Success => GetResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen),
                StatusType.Warning => Brushes.Gold,
                StatusType.Error => GetResourceBrush("ProgressBarRed", Brushes.Crimson),
                _ => GetResourceBrush("SecondaryText", Brushes.Gray)
            };
        }

        // Update tooltip with last agent update time
        var tooltipText = _lastMonitorUpdate == DateTime.MinValue
            ? "Last update: Never"
            : $"Last update: {_lastMonitorUpdate:HH:mm:ss}";

        if (StatusLed != null)
        {
            StatusLed.ToolTip = CreateTopmostAwareToolTip(StatusLed, tooltipText);
        }
        if (StatusText != null)
        {
            StatusText.ToolTip = CreateTopmostAwareToolTip(StatusText, tooltipText);
        }

        var logLevel = type switch
        {
            StatusType.Error => LogLevel.Error,
            StatusType.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };
        _logger.Log(logLevel, "[{StatusType}] {StatusMessage}", type, message);
    }

    private void ApplyMonitorContractStatus(AgentContractHandshakeResult handshakeResult)
    {
        if (handshakeResult.IsCompatible)
        {
            _monitorContractWarningMessage = null;
            return;
        }

        _monitorContractWarningMessage = handshakeResult.Message;
        ShowStatus(handshakeResult.Message, StatusType.Warning);
    }

    private void ShowErrorState(string message)
    {
        if (_usages.Any())
        {
            // Preserve visible data and only surface status when we have a stale snapshot.
            ShowStatus(message, StatusType.Warning);
            return;
        }

        ProvidersList.Children.Clear();
        ProvidersList.Children.Add(CreateInfoTextBlock(message));
        ShowStatus(message, StatusType.Error);
    }

    private TextBlock CreateInfoTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = GetResourceBrush("TertiaryText", Brushes.Gray),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10)
        };
    }

    // Event Handlers
    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RefreshBtn_Click failed");
            ShowStatus("Refresh failed", StatusType.Error);
        }
    }

    private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await OpenSettingsDialogAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SettingsBtn_Click failed");
            ShowStatus("Settings failed", StatusType.Error);
        }
    }

    internal async Task OpenSettingsDialogAsync()
    {
        var settingsDialog = SettingsDialogFactory();
        var settingsWindow = settingsDialog.Dialog;
        if (IsVisible)
        {
            settingsWindow.Owner = this;
            settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        _isSettingsDialogOpen = true;

        try
        {
            _ = ShowOwnedDialog(settingsWindow);
        }
        finally
        {
            _isSettingsDialogOpen = false;
            EnsureAlwaysOnTop();
        }

        if (settingsDialog.HasChanges())
        {
            // Reload preferences and refresh data
            await InitializeAsync();
            // Reapply preferences to update channel selector
            if (_preferencesLoaded)
            {
                ApplyPreferences();
            }
        }
    }

    private static (Window Dialog, Func<bool> HasChanges) CreateDefaultSettingsDialog()
    {
        var settingsWindow = new SettingsWindow();
        return (settingsWindow, () => settingsWindow.SettingsChanged);
    }

    private async void WebBtn_Click(object sender, RoutedEventArgs e)
    {
        await OpenWebUIAsync();
    }

    private async Task OpenWebUIAsync()
    {
        try
        {
            // Start the Web service if not running
            await StartWebServiceAsync();

            // Open browser to the Web UI
            var webUrl = "http://localhost:5100";
            Process.Start(new ProcessStartInfo
            {
                FileName = webUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Web UI");
            MessageBox.Show($"Failed to open Web UI: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StartWebServiceAsync()
    {
        try
        {
            // Check if web service is already running
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            try
            {
                var response = await client.GetAsync("http://localhost:5100");
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Web service already running");
                    return;
                }
            }
            catch
            {
                // Service not running, start it
            }

            // Find Web executable
            var possiblePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIUsageTracker.Web", "bin", "Debug", "net8.0", "AIUsageTracker.Web.exe"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIUsageTracker.Web", "bin", "Release", "net8.0", "AIUsageTracker.Web.exe"),
                Path.Combine(AppContext.BaseDirectory, "AIUsageTracker.Web.exe"),
                // Legacy compatibility
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIUsageTracker.Web", "bin", "Debug", "net8.0", "AIUsageTracker.Web.exe"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIUsageTracker.Web", "bin", "Release", "net8.0", "AIUsageTracker.Web.exe"),
                Path.Combine(AppContext.BaseDirectory, "AIUsageTracker.Web.exe"),
            };

            var webPath = possiblePaths.FirstOrDefault(File.Exists);

            if (webPath == null)
            {
                // Try dotnet run
                var webProjectDir = FindProjectDirectory("AIUsageTracker.Web");
                if (webProjectDir != null)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{webProjectDir}\" --urls \"http://localhost:5100\"",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WorkingDirectory = webProjectDir
                    };
                    Process.Start(psi);
                    _logger.LogInformation("Started Web service via dotnet run");
                    return;
                }

                _logger.LogWarning("Web executable not found");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = webPath,
                Arguments = "--urls \"http://localhost:5100\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(webPath)
            };

            Process.Start(startInfo);
            _logger.LogInformation("Started Web service from: {WebPath}", webPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Web service");
        }
    }

    private static string? FindProjectDirectory(string projectName)
    {
        var currentDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(currentDir);

        while (dir != null)
        {
            var projectPath = Path.Combine(dir.FullName, projectName, $"{projectName}.csproj");
            if (File.Exists(projectPath))
            {
                return Path.GetDirectoryName(projectPath);
            }
            dir = dir.Parent;
        }

        return null;
    }

    private async void PrivacyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var newPrivacyMode = !_isPrivacyMode;
            _preferences.IsPrivacyMode = newPrivacyMode;
            App.SetPrivacyMode(newPrivacyMode);
            await SaveUiPreferencesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PrivacyBtn_Click failed");
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private async void AlwaysOnTop_Checked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsLoaded) return;

            _preferences.AlwaysOnTop = AlwaysOnTopCheck.IsChecked ?? true;
            if (_preferences.AlwaysOnTop)
            {
                EnsureAlwaysOnTop();
            }
            else
            {
                ApplyTopmostState(false);
            }
            await SaveUiPreferencesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AlwaysOnTop_Checked failed");
        }
    }

    private async void Compact_Checked(object sender, RoutedEventArgs e)
    {
       // No-op (Field removed from UI)
       await Task.CompletedTask;
    }

    private async void ShowUsedToggle_Checked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsLoaded) return;

            _preferences.InvertProgressBar = ShowUsedToggle.IsChecked ?? false;
            await SaveUiPreferencesAsync();

            // Refresh the display to show used% vs remaining%
            RenderProviders();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ShowUsedToggle_Checked failed");
        }
    }

    private void RefreshData_NoArgs(object sender, RoutedEventArgs e)
    {
        _ = RefreshDataAsync();
    }

    private void ViewChangelogBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate == null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/rygel/AIConsumptionTracker/releases",
                UseShellExecute = true
            });
            return;
        }

        ShowChangelogWindow(_latestUpdate);
    }

    private void ShowChangelogWindow(UpdateInfo updateInfo)
    {
        var changelogWindow = new Window
        {
            Title = $"Changelog - Version {updateInfo.Version}",
            Width = 680,
            Height = 520,
            MinWidth = 480,
            MinHeight = 320,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = GetResourceBrush("CardBackground", Brushes.Black),
            Foreground = GetResourceBrush("PrimaryText", Brushes.White)
        };

        var viewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            Document = BuildMarkdownDocument(updateInfo.ReleaseNotes)
        };

        changelogWindow.Content = viewer;
        changelogWindow.ShowDialog();
    }

    private FlowDocument BuildMarkdownDocument(string markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            PagePadding = new Thickness(16),
            Background = Brushes.Transparent
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            var emptyParagraph = new Paragraph(new Run("No changelog available for this release."))
            {
                FontStyle = FontStyles.Italic
            };
            document.Blocks.Add(emptyParagraph);
            return document;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCodeBlock = false;
        var codeBuilder = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine ?? string.Empty;
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCodeBlock)
                {
                    AddCodeBlock(document, codeBuilder.ToString().TrimEnd());
                    codeBuilder.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }

                continue;
            }

            if (inCodeBlock)
            {
                codeBuilder.AppendLine(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var headerLevel = GetHeaderLevel(trimmed);
            if (headerLevel > 0)
            {
                var headerText = trimmed[(headerLevel + 1)..];
                var header = new Paragraph
                {
                    Margin = new Thickness(0, headerLevel == 1 ? 10 : 6, 0, 4),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = headerLevel switch
                    {
                        1 => 22,
                        2 => 18,
                        3 => 16,
                        _ => 14
                    }
                };
                AddMarkdownInlines(header, headerText);
                document.Blocks.Add(header);
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                var bullet = new Paragraph
                {
                    Margin = new Thickness(0, 1, 0, 1)
                };
                bullet.Inlines.Add(new Run("• "));
                AddMarkdownInlines(bullet, trimmed[2..]);
                document.Blocks.Add(bullet);
                continue;
            }

            if (TryParseNumberedItem(trimmed, out var numberedPrefix, out var numberedText))
            {
                var numbered = new Paragraph
                {
                    Margin = new Thickness(0, 1, 0, 1)
                };
                numbered.Inlines.Add(new Run($"{numberedPrefix}. "));
                AddMarkdownInlines(numbered, numberedText);
                document.Blocks.Add(numbered);
                continue;
            }

            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 6),
                LineHeight = 20
            };
            AddMarkdownInlines(paragraph, trimmed);
            document.Blocks.Add(paragraph);
        }

        if (inCodeBlock && codeBuilder.Length > 0)
        {
            AddCodeBlock(document, codeBuilder.ToString().TrimEnd());
        }

        return document;
    }

    private static int GetHeaderLevel(string trimmedLine)
    {
        var level = 0;
        while (level < trimmedLine.Length && trimmedLine[level] == '#')
        {
            level++;
        }

        return level > 0 && level < trimmedLine.Length && trimmedLine[level] == ' ' ? level : 0;
    }

    private static bool TryParseNumberedItem(string line, out int number, out string content)
    {
        number = 0;
        content = string.Empty;

        var dotIndex = line.IndexOf(". ", StringComparison.Ordinal);
        if (dotIndex <= 0)
        {
            return false;
        }

        var prefix = line[..dotIndex];
        if (!int.TryParse(prefix, System.Globalization.CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        content = line[(dotIndex + 2)..];
        return !string.IsNullOrWhiteSpace(content);
    }

    private void AddCodeBlock(FlowDocument document, string codeText)
    {
        var codeParagraph = new Paragraph(new Run(codeText))
        {
            Margin = new Thickness(0, 6, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = GetResourceBrush("FooterBackground", Brushes.Black),
            Foreground = GetResourceBrush("PrimaryText", Brushes.White)
        };
        document.Blocks.Add(codeParagraph);
    }

    private void AddMarkdownInlines(Paragraph paragraph, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var matches = MarkdownTokenRegex.Matches(text);
        var cursor = 0;

        foreach (Match match in matches)
        {
            if (match.Index > cursor)
            {
                paragraph.Inlines.Add(new Run(text[cursor..match.Index]));
            }

            var token = match.Value;
            if (TryCreateHyperlink(token, out var hyperlink))
            {
                paragraph.Inlines.Add(hyperlink);
            }
            else if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Bold(new Run(token[2..^2])));
            }
            else if (token.StartsWith("*", StringComparison.Ordinal) && token.EndsWith("*", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Italic(new Run(token[1..^1])));
            }
            else if (token.StartsWith("`", StringComparison.Ordinal) && token.EndsWith("`", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = GetResourceBrush("FooterBackground", Brushes.Black)
                });
            }
            else
            {
                paragraph.Inlines.Add(new Run(token));
            }

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
        {
            paragraph.Inlines.Add(new Run(text[cursor..]));
        }
    }

    private bool TryCreateHyperlink(string token, out Hyperlink hyperlink)
    {
        hyperlink = null!;

        if (!token.StartsWith("[", StringComparison.Ordinal) || !token.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var separator = token.IndexOf("](", StringComparison.Ordinal);
        if (separator <= 1)
        {
            return false;
        }

        var text = token[1..separator];
        var url = token[(separator + 2)..^1];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        hyperlink = new Hyperlink(new Run(text))
        {
            NavigateUri = uri
        };
        hyperlink.Click += (s, e) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        };

        return true;
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            _latestUpdate = await _updateChecker.CheckForUpdatesAsync();

            if (_latestUpdate != null)
            {
                if (UpdateNotificationBanner != null && UpdateText != null)
                {
                    UpdateText.Text = $"New version available: {_latestUpdate.Version}";
                    UpdateNotificationBanner.Visibility = Visibility.Visible;
                }
            }
            else if (UpdateNotificationBanner != null)
            {
                UpdateNotificationBanner.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
        }
    }

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate == null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/rygel/AIConsumptionTracker/releases/latest",
                UseShellExecute = true
            });
            return;
        }

        var result = MessageBox.Show(
            $"Download and install version {_latestUpdate.Version}?\n\nThe application will restart after installation.",
            "Confirm Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        Window? progressWindow = null;

        try
        {
            var progressBar = new ProgressBar
            {
                Height = 20,
                Minimum = 0,
                Maximum = 100
            };

            progressWindow = new Window
            {
                Title = "Downloading Update",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = $"Downloading version {_latestUpdate.Version}...", Margin = new Thickness(0, 0, 0, 10) },
                        progressBar
                    }
                }
            };

            var progress = new Progress<double>(p => progressBar.Value = p);
            progressWindow.Show();

            var success = await _updateChecker.DownloadAndInstallUpdateAsync(_latestUpdate, progress);
            progressWindow.Close();
            progressWindow = null;

            if (success)
            {
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show(
                    "Failed to download or install the update. Please try again or download manually from the releases page.",
                    "Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            progressWindow?.Close();
            MessageBox.Show($"Update error: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private async Task RestartMonitorAsync()
    {
        try
        {
            ShowStatus("Restarting monitor...", StatusType.Warning);

            // Try to start agent
            if (await MonitorLauncher.StartAgentAsync())
            {
                var monitorReady = await MonitorLauncher.WaitForAgentAsync();
                if (monitorReady)
                {
                    ShowStatus("Monitor restarted", StatusType.Success);
                    await RefreshDataAsync();
                }
                else
                {
                    ShowStatus("Monitor restart failed", StatusType.Error);
                }
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Restart error: {ex.Message}", StatusType.Error);
        }
    }


    private async void MonitorToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, _) = await MonitorLauncher.IsAgentRunningWithPortAsync();

            if (isRunning)
            {
                // Stop the agent
                ShowStatus("Stopping monitor...", StatusType.Warning);
                var stopped = await MonitorLauncher.StopAgentAsync();
                if (stopped)
                {
                    ShowStatus("Monitor stopped", StatusType.Info);
                    UpdateMonitorToggleButton(false);
                }
                else
                {
                    ShowStatus("Failed to stop monitor", StatusType.Error);
                }
            }
            else
            {
                // Start the monitor
                ShowStatus("Starting monitor...", StatusType.Warning);
                if (await MonitorLauncher.StartAgentAsync())
                {
                    var monitorReady = await MonitorLauncher.WaitForAgentAsync();
                    if (monitorReady)
                    {
                        ShowStatus("Monitor started", StatusType.Success);
                        UpdateMonitorToggleButton(true);
                        await RefreshDataAsync();
                    }
                    else
                    {
                        ShowStatus("Monitor failed to start", StatusType.Error);
                        UpdateMonitorToggleButton(false);
                    }
                }
                else
                {
                    ShowStatus("Could not start monitor", StatusType.Error);
                    UpdateMonitorToggleButton(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MonitorToggleBtn_Click failed");
            ShowStatus("Monitor toggle failed", StatusType.Error);
        }
    }

    private void UpdateMonitorToggleButton(bool isRunning)
    {
        if (MonitorToggleBtn != null && MonitorToggleIcon != null)
        {
            // Update icon: Play (E768) when stopped, Stop (E71A) when running
            MonitorToggleIcon.Text = isRunning ? "\uE71A" : "\uE768";
            MonitorToggleBtn.ToolTip = isRunning ? "Stop Monitor" : "Start Monitor";
        }
    }

    private async Task UpdateMonitorToggleButtonStateAsync()
    {
        var (isRunning, _) = await MonitorLauncher.IsAgentRunningWithPortAsync();
        Dispatcher.Invoke(() => UpdateMonitorToggleButton(isRunning));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.R:
                    RefreshBtn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.P:
                    PrivacyBtn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Q:
                    CloseBtn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.Escape)
        {
            CloseBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            SettingsBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
            e.Handled = true;
        }
    }


}
