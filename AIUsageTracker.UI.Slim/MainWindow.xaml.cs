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
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
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
        RegexOptions.Compiled);

    private readonly MonitorService _agentService;
    private IUpdateCheckerService _updateChecker;
    private AppPreferences _preferences = new();
    private List<ProviderUsage> _usages = new();
    private List<ProviderConfig> _configs = new();
    private bool _isPrivacyMode = App.IsPrivacyMode;
    private bool _isLoading = false;
    private readonly Dictionary<string, ImageSource> _iconCache = new();
    private DateTime _lastAgentUpdate = DateTime.MinValue;
    private DateTime _lastRefreshTrigger = DateTime.MinValue;
    private const int RefreshCooldownSeconds = 120; // Only trigger refresh if 2 minutes since last attempt
    private DispatcherTimer? _pollingTimer;
    private string? _agentContractWarningMessage;
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

    public MainWindow()
        : this(skipUiInitialization: false)
    {
    }

    internal MainWindow(bool skipUiInitialization)
    {
        if (!skipUiInitialization)
        {
            InitializeComponent();
        }

        _agentService = new MonitorService();
        _updateChecker = new GitHubUpdateChecker(NullLogger<GitHubUpdateChecker>.Instance, UpdateChannel.Stable);
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

        _updateCheckTimer.Tick += async (s, e) => await CheckForUpdatesAsync();
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

        // Set version text
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        if (version != null)
        {
            VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        }

        Loaded += async (s, e) =>
        {
            PositionWindowNearTray();
            await InitializeAsync();
            _ = CheckForUpdatesAsync();
        };

        // Track window position changes
        LocationChanged += async (s, e) => await SaveWindowPositionAsync();
        SizeChanged += async (s, e) => await SaveWindowPositionAsync();
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
        Debug.WriteLine(message);
        Console.WriteLine(message);
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
        if (_isLoading || _agentService == null)
            return;

        try
        {
            _isLoading = true;

            if (!_preferencesLoaded)
            {
                _preferences = await UiPreferencesStore.LoadAsync();
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
                    await _agentService.RefreshPortAsync();
                    
                    // Check if Monitor is running on the discovered port
                    var isRunning = await _agentService.CheckHealthAsync();
                    
                    if (!isRunning)
                    {
                        Dispatcher.Invoke(() => ShowStatus("Monitor not running. Starting monitor...", StatusType.Warning));

                        if (await MonitorLauncher.StartAgentAsync())
                        {
                            Dispatcher.Invoke(() => ShowStatus("Waiting for monitor...", StatusType.Warning));
                            var agentReady = await MonitorLauncher.WaitForAgentAsync();

                            if (!agentReady)
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

                    // Update agent toggle button state
                    await UpdateAgentToggleButtonStateAsync();

                    return true;
                } catch (Exception ex) {
                    Debug.WriteLine($"Error in background initialization: {ex.Message}");
                    return false;
                }
            });

            if (success)
            {
                var handshakeResult = await _agentService.CheckApiContractAsync();
                ApplyAgentContractStatus(handshakeResult);

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
        const int maxAttempts = 30; // 30 attempts * 2 seconds = 60 seconds max
        const int pollIntervalMs = 2000; // 2 seconds between attempts

        ShowStatus("Loading data...", StatusType.Info);

        // First, check if Monitor is reachable
        var isHealthy = await _agentService.CheckHealthAsync();
        if (!isHealthy)
        {
            ShowStatus("Monitor not reachable", StatusType.Error);
            ShowErrorState("Cannot connect to Monitor.\n\nPlease ensure:\n1. Monitor is running\n2. Port is correct (check monitor.json)\n3. Firewall is not blocking\n\nTry restarting the Monitor.");
            return;
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                // Try to get cached data from agent
                var usages = await _agentService.GetUsageAsync();

                // Filter out placeholder data (safety filter)
                var usableUsages = usages.Where(u => 
                    u.RequestsAvailable > 0 || u.RequestsUsed > 0 || u.IsAvailable
                ).ToList();

                if (usableUsages.Any())
                {
                    // Data is available - render and stop rapid polling
                    _usages = usableUsages;
                    RenderProviders();
                    await UpdateTrayIconsAsync();
                    _lastAgentUpdate = DateTime.Now;
                    ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                    return;
                }

                // No data available - trigger refresh on first attempt if cooldown has passed
                // This is needed for fresh installs where Monitor's background refresh hasn't completed yet
                var secondsSinceLastRefresh = (DateTime.Now - _lastRefreshTrigger).TotalSeconds;
                if (attempt == 0 && secondsSinceLastRefresh >= RefreshCooldownSeconds)
                {
                    Debug.WriteLine("No data on first poll, triggering provider refresh...");
                    _lastRefreshTrigger = DateTime.Now;
                    try
                    {
                        await _agentService.TriggerRefreshAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Trigger refresh failed: {ex.Message}");
                    }
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
                Debug.WriteLine($"Connection error during rapid polling: {ex.Message}");
                ShowStatus("Connection lost", StatusType.Error);
                ShowErrorState($"Lost connection to Monitor:\n{ex.Message}\n\nTry refreshing or restarting the Monitor.");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during rapid polling: {ex.Message}");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(pollIntervalMs);
                }
            }
        }

        // Max attempts reached - show error or empty state
        ShowStatus("No data available", StatusType.Error);
        ShowErrorState("No provider data available.\n\nThe Monitor may still be initializing.\nTry refreshing manually or check Settings > Monitor.");
    }

    private void ApplyPreferences()
    {
        // Apply window settings
        this.Topmost = _preferences.AlwaysOnTop;
        this.Width = _preferences.WindowWidth;
        this.Height = _preferences.WindowHeight;
        PositionWindowNearTray();

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
        var saved = await UiPreferencesStore.SaveAsync(_preferences);
        if (!saved)
        {
            Debug.WriteLine("Failed to save Slim UI preferences.");
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
            Debug.WriteLine($"[WINDOW] SetWindowPos failed err={win32Error} alwaysOnTop={alwaysOnTop} noActivate={noActivate}");
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
            _preferences = new AppPreferences
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
                IsPrivacyMode = true
            };

            App.Preferences = _preferences;
            _isPrivacyMode = true;
            App.SetPrivacyMode(true);
            _preferencesLoaded = true;
            _lastAgentUpdate = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Local);
            var deterministicNow = DateTime.Now;
            ApplyPreferences();
            Width = 460;
            Height = MinHeight;

            _usages = new List<ProviderUsage>
            {
                new()
                {
                    ProviderId = "antigravity",
                    ProviderName = "Antigravity",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    DisplayAsFraction = true,
                    RequestsPercentage = 60.0,
                    RequestsUsed = 40,
                    RequestsAvailable = 100,
                    Description = "60.0% Remaining",
                    IsAvailable = true,
                    AuthSource = "local app",
                    Details = new List<ProviderUsageDetail>
                    {
                        new()
                        {
                            Name = "Claude Opus 4.6 (Thinking)",
                            ModelName = "Claude Opus 4.6 (Thinking)",
                            GroupName = "Recommended Group 1",
                            Used = "60%",
                            Description = "60% remaining",
                            NextResetTime = deterministicNow.AddHours(10)
                        },
                        new()
                        {
                            Name = "Claude Sonnet 4.6 (Thinking)",
                            ModelName = "Claude Sonnet 4.6 (Thinking)",
                            GroupName = "Recommended Group 1",
                            Used = "60%",
                            Description = "60% remaining",
                            NextResetTime = deterministicNow.AddHours(10)
                        },
                        new()
                        {
                            Name = "Gemini 3 Flash",
                            ModelName = "Gemini 3 Flash",
                            GroupName = "Recommended Group 1",
                            Used = "100%",
                            Description = "100% remaining",
                            NextResetTime = deterministicNow.AddHours(6)
                        },
                        new()
                        {
                            Name = "Gemini 3.1 Pro (High)",
                            ModelName = "Gemini 3.1 Pro (High)",
                            GroupName = "Recommended Group 1",
                            Used = "100%",
                            Description = "100% remaining",
                            NextResetTime = deterministicNow.AddHours(14)
                        },
                        new()
                        {
                            Name = "Gemini 3.1 Pro (Low)",
                            ModelName = "Gemini 3.1 Pro (Low)",
                            GroupName = "Recommended Group 1",
                            Used = "100%",
                            Description = "100% remaining",
                            NextResetTime = deterministicNow.AddHours(14)
                        },
                        new()
                        {
                            Name = "GPT-OSS 120B (Medium)",
                            ModelName = "GPT-OSS 120B (Medium)",
                            GroupName = "Recommended Group 1",
                            Used = "60%",
                            Description = "60% remaining",
                            NextResetTime = deterministicNow.AddHours(8)
                        }
                    }
                },
                new()
                {
                    ProviderId = "github-copilot",
                    ProviderName = "GitHub Copilot",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    DisplayAsFraction = true,
                    RequestsPercentage = 72.5,
                    RequestsUsed = 110,
                    RequestsAvailable = 400,
                    Description = "72.5% Remaining",
                    IsAvailable = true,
                    AuthSource = "oauth",
                    NextResetTime = deterministicNow.AddHours(20)
                },
                new()
                {
                    ProviderId = "zai-coding-plan",
                    ProviderName = "Z.AI",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    DisplayAsFraction = true,
                    RequestsPercentage = 82.0,
                    RequestsUsed = 45,
                    RequestsAvailable = 250,
                    Description = "82.0% Remaining",
                    IsAvailable = true,
                    AuthSource = "api key",
                    NextResetTime = deterministicNow.AddHours(12)
                },
                new()
                {
                    ProviderId = "claude-code",
                    ProviderName = "Claude Code",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    RequestsPercentage = 0,
                    RequestsUsed = 0,
                    RequestsAvailable = 0,
                    Description = "Connected",
                    IsAvailable = true,
                    AuthSource = "local credentials"
                },
                new()
                {
                    ProviderId = "synthetic",
                    ProviderName = "Synthetic",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    DisplayAsFraction = true,
                    RequestsPercentage = 91.0,
                    RequestsUsed = 18,
                    RequestsAvailable = 200,
                    Description = "91.0% Remaining",
                    IsAvailable = true,
                    AuthSource = "api key",
                    NextResetTime = deterministicNow.AddHours(4)
                },
                new()
                {
                    ProviderId = "mistral",
                    ProviderName = "Mistral",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    RequestsPercentage = 0,
                    RequestsUsed = 0,
                    RequestsAvailable = 0,
                    Description = "Connected",
                    IsAvailable = true,
                    AuthSource = "api key"
                }
            };

            RenderProviders();
            ShowStatus($"{_lastAgentUpdate:HH:mm:ss}", StatusType.Success);
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
        if (_isLoading || _agentService == null)
            return;

        try
        {
            _isLoading = true;
            ShowStatus("Refreshing...", StatusType.Info);

            // Trigger refresh on agent
            await _agentService.TriggerRefreshAsync();

            // Get updated usage data
            _usages = await _agentService.GetUsageAsync();

            // Render providers
            RenderProviders();
            await UpdateTrayIconsAsync();

            ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
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
        ProvidersList.Children.Clear();

        if (!_usages.Any())
        {
            ProvidersList.Children.Add(CreateInfoTextBlock("No provider data available."));
            ApplyProviderListFontPreferences();
            return;
        }

        var filteredUsages = _usages.ToList();

        // Filter out Antigravity completely if not available.
        // Filter out antigravity.* items from API payload because model rows are rendered from Antigravity details.
        filteredUsages = filteredUsages.Where(u =>
            !(u.ProviderId == "antigravity" && !u.IsAvailable) &&
            !u.ProviderId.StartsWith("antigravity.")
        ).ToList();

        // Guard against duplicate provider entries returned by the Agent.
        filteredUsages = filteredUsages
            .GroupBy(u => u.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Separate providers by type and order alphabetically
        var quotaProviders = filteredUsages
            .Where(u => u.IsQuotaBased || u.PlanType == PlanType.Coding)
            .OrderBy(GetFriendlyProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var paygProviders = filteredUsages
            .Where(u => !u.IsQuotaBased && u.PlanType != PlanType.Coding)
            .OrderBy(GetFriendlyProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Plans & Quotas Section
        if (quotaProviders.Any())
        {
            var (plansHeader, plansContainer) = CreateCollapsibleGroupHeader(
                "Plans & Quotas",
                Brushes.DeepSkyBlue,
                "PlansAndQuotas",
                () => _preferences.IsPlansAndQuotasCollapsed,
                v => _preferences.IsPlansAndQuotasCollapsed = v);

            ProvidersList.Children.Add(plansHeader);
            ProvidersList.Children.Add(plansContainer);

            if (!_preferences.IsPlansAndQuotasCollapsed)
            {
                // Add all quota providers with Antigravity models listed as standalone cards.
                foreach (var usage in quotaProviders)
                {
                    if (usage.ProviderId.Equals("antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        if (usage.Details?.Any() == true)
                        {
                            AddAntigravityModels(usage, plansContainer);
                        }
                        else
                        {
                            AddAntigravityUnavailableNotice(usage, plansContainer);
                        }

                        continue;
                    }

                    AddProviderCard(usage, plansContainer);

                    if (usage.Details?.Any() == true)
                    {
                        AddCollapsibleSubProviders(usage, plansContainer);
                    }
                }
            }
        }

        // Pay As You Go Section
        if (paygProviders.Any())
        {
            var (paygHeader, paygContainer) = CreateCollapsibleGroupHeader(
                "Pay As You Go",
                Brushes.MediumSeaGreen,
                "PayAsYouGo",
                () => _preferences.IsPayAsYouGoCollapsed,
                v => _preferences.IsPayAsYouGoCollapsed = v);

            ProvidersList.Children.Add(paygHeader);
            ProvidersList.Children.Add(paygContainer);

            if (!_preferences.IsPayAsYouGoCollapsed)
            {
                foreach (var usage in paygProviders)
                {
                    AddProviderCard(usage, paygContainer);
                }
            }
        }

        ApplyProviderListFontPreferences();
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
        var titleText = isGroupHeader ? title.ToUpper() : title;
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
            isGroupHeader ? 10.0 : 10.0,
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

    private (UIElement Header, StackPanel Container) CreateCollapsibleGroupHeader(
        string title, Brush accent, string groupKey,
        Func<bool> getCollapsed, Action<bool> setCollapsed)
    {
        return CreateCollapsibleHeader(title, accent, isGroupHeader: true, groupKey, getCollapsed, setCollapsed);
    }

    private (UIElement Header, StackPanel Container) CreateCollapsibleSubHeader(
        string title, Brush accent,
        Func<bool> getCollapsed, Action<bool> setCollapsed)
    {
        return CreateCollapsibleHeader(title, accent, isGroupHeader: false, null, getCollapsed, setCollapsed);
    }

    private void AddProviderCard(ProviderUsage usage, StackPanel container, bool isChild = false)
    {
        var friendlyName = GetFriendlyProviderName(usage);

        // Compact horizontal bar similar to non-slim UI
        bool isMissing = usage.Description.Contains("not found", StringComparison.OrdinalIgnoreCase);
        bool isConsoleCheck = usage.Description.Contains("Check Console", StringComparison.OrdinalIgnoreCase);
        bool isError = usage.Description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);
        bool isUnknown = usage.Description.Contains("unknown", StringComparison.OrdinalIgnoreCase);
        bool isAntigravityParent = usage.ProviderId.Equals("antigravity", StringComparison.OrdinalIgnoreCase);

        // Main Grid Container - single row layout
        var grid = new Grid
        {
            Margin = new Thickness(isChild ? 20 : 0, 0, 0, 2),
            Height = 24,
            Background = Brushes.Transparent,
            Tag = usage.ProviderId
        };

        bool shouldHaveProgress = usage.IsAvailable &&
            !isUnknown &&
            !isAntigravityParent &&
            (usage.RequestsPercentage > 0 || usage.IsQuotaBased) &&
            !isMissing &&
            !isError;

        // Background Progress Bar
        var pGrid = new Grid();

        // Normalize percentages based on provider type
        // Quota/Coding: RequestsPercentage is REMAINING %
        // Usage/PAYG: RequestsPercentage is USED %
        bool isQuotaType = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
        double pctRemaining = isQuotaType ? usage.RequestsPercentage : Math.Max(0, 100 - usage.RequestsPercentage);
        double pctUsed = isQuotaType ? Math.Max(0, 100 - usage.RequestsPercentage) : usage.RequestsPercentage;

        // Determine which width to show based on toggle
        bool showUsed = ShowUsedToggle?.IsChecked ?? false;

        if (TryGetDualWindowUsedPercentages(usage, out var hourlyUsed, out var weeklyUsed))
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
            var indicatorWidth = showUsed ? pctUsed : pctRemaining;

            // Clamp to 0-100
            indicatorWidth = Math.Max(0, Math.Min(100, indicatorWidth));

            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indicatorWidth, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star) });

            var fill = new Border
            {
                Background = GetProgressBarColor(pctUsed),
                Opacity = 0.45,
                CornerRadius = new CornerRadius(0)
            };
            pGrid.Children.Add(fill);
        }

        pGrid.Visibility = shouldHaveProgress ? Visibility.Visible : Visibility.Collapsed;
        grid.Children.Add(pGrid);

        // Background for non-progress items
        var bg = new Border
        {
            Background = GetResourceBrush("CardBackground", Brushes.DarkGray),
            CornerRadius = new CornerRadius(0),
            Visibility = shouldHaveProgress ? Visibility.Collapsed : Visibility.Visible
        };
        grid.Children.Add(bg);

        // Content Overlay
        var contentPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 6, 0) };

        // Provider icon or bullet for child items
        if (isChild)
        {
            var icon = new Border
            {
                Width = 4, Height = 4,
                Background = GetResourceBrush("SecondaryText", Brushes.Gray),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(2, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            contentPanel.Children.Add(icon);
            DockPanel.SetDock(icon, Dock.Left);
        }
        else
        {
            // Provider icon for parent items
            var providerIcon = CreateProviderIcon(usage.ProviderId);
            providerIcon.Margin = new Thickness(0, 0, 6, 0); // Reduced margin for specific alignment
            providerIcon.Width = 14;
            providerIcon.Height = 14;
            providerIcon.VerticalAlignment = VerticalAlignment.Center;
            contentPanel.Children.Add(providerIcon);
            DockPanel.SetDock(providerIcon, Dock.Left);
        }

        // Right Side: Usage/Status
        var statusText = "";
        Brush statusBrush = GetResourceBrush("SecondaryText", Brushes.Gray);

        if (isMissing) { statusText = "Key Missing"; statusBrush = Brushes.IndianRed; }
        else if (isError) { statusText = "Error"; statusBrush = Brushes.Red; }
        else if (isConsoleCheck) { statusText = "Check Console"; statusBrush = Brushes.Orange; }
        else
        {
            statusText = usage.Description;
            var isStatusOnlyProvider =
                usage.ProviderId.Equals("mistral", StringComparison.OrdinalIgnoreCase) ||
                usage.ProviderId.Equals("cloud-code", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(usage.UsageUnit, "Status", StringComparison.OrdinalIgnoreCase);

            if (isAntigravityParent)
            {
                statusText = string.IsNullOrWhiteSpace(usage.Description)
                    ? "Per-model quotas"
                    : usage.Description;
            }
            else if (!isUnknown && !isStatusOnlyProvider && usage.PlanType == PlanType.Coding)
            {
                var displayUsed = ShowUsedToggle?.IsChecked ?? false;

                // Check if we have raw numbers (limit > 100 serves as a heuristic for usage limits > 100%)
                if (usage.DisplayAsFraction)
                {
                    if (displayUsed)
                    {
                        statusText = $"{usage.RequestsUsed:N0} / {usage.RequestsAvailable:N0} used";
                    }
                    else
                    {
                        var remaining = usage.RequestsAvailable - usage.RequestsUsed;
                        statusText = $"{remaining:N0} / {usage.RequestsAvailable:N0} remaining";
                    }
                }
                else
                {
                    // Percentage only mode
                    var remainingPercent = UsageMath.ClampPercent(usage.RequestsPercentage);
                    if (displayUsed)
                    {
                        statusText = $"{(100.0 - remainingPercent):F0}% used";
                    }
                    else
                    {
                        statusText = $"{remainingPercent:F0}% remaining";
                    }
                }
            }
            else if (!isUnknown && !isStatusOnlyProvider && usage.PlanType == PlanType.Usage && usage.RequestsAvailable > 0)
            {
                var showUsedPercent = ShowUsedToggle?.IsChecked ?? false;
                var usedPercent = UsageMath.ClampPercent(usage.RequestsPercentage);
                statusText = showUsedPercent
                    ? $"{usedPercent:F0}% used"
                    : $"{(100.0 - usedPercent):F0}% remaining";
            }
            else if (!isUnknown && !isStatusOnlyProvider && (usage.IsQuotaBased || usage.PlanType == PlanType.Coding))
            {
                // Show used% or remaining% based on toggle
                // Show used% or remaining% based on toggle (variable renamed to avoid conflict)
                var usePercentage = ShowUsedToggle?.IsChecked ?? false;
                if (usePercentage)
                {
                    var usedPercent = 100.0 - usage.RequestsPercentage;
                    statusText = $"{usedPercent:F0}% used";
                }
                else
                {
                    statusText = $"{usage.RequestsPercentage:F0}% remaining";
                }
            }
        }

        // Reset time display (if available) - shown with muted golden color
        if (usage.NextResetTime.HasValue)
        {
            var relative = GetRelativeTimeString(usage.NextResetTime.Value);
            var resetBlock = new TextBlock
            {
                Text = $"(Resets: {relative})",
                FontSize = 10,
                Foreground = GetResourceBrush("StatusTextWarning", Brushes.Goldenrod),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,

                Margin = new Thickness(10, 0, 0, 0)
            };
            DockPanel.SetDock(resetBlock, Dock.Right);
            contentPanel.Children.Add(resetBlock);
        }

        // Right Side: Usage/Status - must be added last to Dock.Right to appear left of reset time
        var rightBlock = new TextBlock
        {
            Text = statusText,
            FontSize = 10,
            Foreground = statusBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        DockPanel.SetDock(rightBlock, Dock.Right);
        contentPanel.Children.Add(rightBlock);

        // Name (gets remaining space)
        var accountPart = string.IsNullOrWhiteSpace(usage.AccountName) ? "" : $" [{(_isPrivacyMode ? MaskAccountIdentifier(usage.AccountName) : usage.AccountName)}]";
        var nameBlock = new TextBlock
        {
            Text = $"{friendlyName}{accountPart}",
            FontWeight = isChild ? FontWeights.Normal : FontWeights.SemiBold,
            FontSize = 11,
            Foreground = isMissing ? GetResourceBrush("TertiaryText", Brushes.Gray) : GetResourceBrush("PrimaryText", Brushes.White),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        contentPanel.Children.Add(nameBlock);
        DockPanel.SetDock(nameBlock, Dock.Left);

        grid.Children.Add(contentPanel);

        // Tooltip with details
        if (usage.Details != null && usage.Details.Any())
        {
            var tooltipBuilder = new System.Text.StringBuilder();
            tooltipBuilder.AppendLine($"{friendlyName}");
            tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
            if (!string.IsNullOrEmpty(usage.Description))
            {
                tooltipBuilder.AppendLine($"Description: {usage.Description}");
            }
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine("Rate Limits:");
            foreach (var detail in usage.Details.OrderBy(GetDetailDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                tooltipBuilder.AppendLine($"  {GetDetailDisplayName(detail)}: {detail.Used}");
            }
            
            var toolTip = new ToolTip
            {
                Content = tooltipBuilder.ToString().Trim(),
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                PlacementTarget = grid
            };
            
            // Hook into the tooltip opened event to ensure it stays on top
            toolTip.Opened += (s, e) =>
            {
                _isTooltipOpen = true;
                if (s is ToolTip tip && tip.PlacementTarget != null)
                {
                    // Force the tooltip window to be topmost when parent is topmost
                    var tooltipWindow = Window.GetWindow(tip);
                    if (tooltipWindow != null && this.Topmost)
                    {
                        tooltipWindow.Topmost = true;
                    }
                }
            };
            toolTip.Closed += (s, e) => _isTooltipOpen = false;
            
            grid.ToolTip = toolTip;
            ToolTipService.SetInitialShowDelay(grid, 100);
            ToolTipService.SetShowDuration(grid, 15000);
        }
        else if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            var toolTip = new ToolTip
            {
                Content = $"{friendlyName}\nSource: {usage.AuthSource}",
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                PlacementTarget = grid
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
            
            grid.ToolTip = toolTip;
            ToolTipService.SetInitialShowDelay(grid, 100);
            ToolTipService.SetShowDuration(grid, 15000);
        }

        container.Children.Add(grid);
    }

    private static string GetFriendlyProviderName(ProviderUsage usage)
    {
        var fromPayload = usage.ProviderName;
        if (!string.IsNullOrWhiteSpace(fromPayload) &&
            !string.Equals(fromPayload, usage.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return fromPayload;
        }

        return usage.ProviderId.ToLowerInvariant() switch
        {
            "antigravity" => "Google Antigravity",
            "gemini-cli" => "Google Gemini",
            "github-copilot" => "GitHub Copilot",
            "openai" => "OpenAI (Codex)",
            "minimax" => "Minimax (China)",
            "minimax-io" => "Minimax (International)",
            "opencode" => "OpenCode",
            "claude-code" => "Claude Code",
            "zai-coding-plan" => "Z.ai Coding Plan",
            _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                usage.ProviderId.Replace("_", " ").Replace("-", " "))
        };
    }

    private void AddAntigravityModels(ProviderUsage usage, StackPanel container)
    {
        if (usage.Details?.Any() != true)
        {
            return;
        }

        var uniqueModelDetails = usage.Details
            .Where(d => !string.IsNullOrWhiteSpace(GetAntigravityModelDisplayName(d)) && !d.Name.StartsWith("[", StringComparison.Ordinal))
            .GroupBy(GetAntigravityModelDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());

        foreach (var detail in uniqueModelDetails.OrderBy(GetAntigravityModelDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            AddProviderCard(CreateAntigravityModelUsage(detail, usage), container);
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

    private static ProviderUsage CreateAntigravityModelUsage(ProviderUsageDetail detail, ProviderUsage parentUsage)
    {
        var remainingPercent = ParsePercent(detail.Used);
        var hasRemainingPercent = remainingPercent.HasValue;
        var effectiveRemaining = remainingPercent ?? 0;
        return new ProviderUsage
        {
            ProviderId = $"antigravity.{detail.Name.ToLowerInvariant().Replace(" ", "-")}",
            ProviderName = $"{GetAntigravityModelDisplayName(detail)} [Antigravity]",
            RequestsPercentage = effectiveRemaining,
            RequestsUsed = 100.0 - effectiveRemaining,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = hasRemainingPercent ? $"{effectiveRemaining:F0}% Remaining" : "Usage unknown",
            NextResetTime = detail.NextResetTime,
            IsAvailable = parentUsage.IsAvailable,
            AuthSource = parentUsage.AuthSource
        };
    }

    private static double? ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsedValue = value.Replace("%", "").Trim();
        return double.TryParse(parsedValue, out var parsed)
            ? Math.Max(0, Math.Min(100, parsed))
            : null;
    }

    private static bool TryGetDualWindowUsedPercentages(ProviderUsage usage, out double hourlyUsed, out double weeklyUsed)
    {
        hourlyUsed = 0;
        weeklyUsed = 0;

        if (usage.Details?.Any() != true)
        {
            return false;
        }

        var hourlyDetail = usage.Details.FirstOrDefault(d => d.Name.Equals("5-hour quota", StringComparison.OrdinalIgnoreCase));
        var weeklyDetail = usage.Details.FirstOrDefault(d => d.Name.Equals("Weekly quota", StringComparison.OrdinalIgnoreCase));

        if (hourlyDetail == null || weeklyDetail == null)
        {
            return false;
        }

        var parsedHourly = ParseUsedPercentFromDetail(hourlyDetail.Used);
        var parsedWeekly = ParseUsedPercentFromDetail(weeklyDetail.Used);
        if (!parsedHourly.HasValue || !parsedWeekly.HasValue)
        {
            return false;
        }

        hourlyUsed = parsedHourly.Value;
        weeklyUsed = parsedWeekly.Value;
        return true;
    }

    private static double? ParseUsedPercentFromDetail(string? used)
    {
        if (string.IsNullOrWhiteSpace(used))
        {
            return null;
        }

        // First try to find percentage followed by "used" (e.g., "100% used")
        var usedMatch = Regex.Match(used, @"(?<percent>\d+(?:\.\d+)?)\s*%\s*used", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (usedMatch.Success)
        {
            if (double.TryParse(
                    usedMatch.Groups["percent"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var percent))
            {
                return Math.Clamp(percent, 0, 100);
            }
        }

        // Fallback: match first percentage (for backwards compatibility)
        var match = Regex.Match(used, @"(?<percent>\d+(?:\.\d+)?)\s*%", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        if (!double.TryParse(
                match.Groups["percent"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var fallbackPercent))
        {
            return null;
        }

        return Math.Clamp(fallbackPercent, 0, 100);
    }

    private Grid CreateProgressLayer(double usedPercent, bool showUsed, double opacity)
    {
        var remainingPercent = Math.Max(0, 100 - usedPercent);
        var indicatorWidth = showUsed ? usedPercent : remainingPercent;
        indicatorWidth = Math.Clamp(indicatorWidth, 0, 100);

        var layer = new Grid();
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indicatorWidth, GridUnitType.Star) });
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star) });

        var fill = new Border
        {
            Background = GetProgressBarColor(usedPercent),
            Opacity = opacity,
            CornerRadius = new CornerRadius(0)
        };

        layer.Children.Add(fill);
        return layer;
    }

    private static string GetAntigravityModelDisplayName(ProviderUsageDetail detail)
    {
        return detail.Name;
    }

    private static string GetDetailDisplayName(ProviderUsageDetail detail)
    {
        return detail.Name;
    }

    private static bool IsDisplayableSubProviderDetail(ProviderUsageDetail detail)
    {
        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            return false;
        }

        if (IsWindowQuotaDetail(detail.Name))
        {
            return false;
        }

        return !detail.Name.Contains("window", StringComparison.OrdinalIgnoreCase) &&
               !detail.Name.Contains("credit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowQuotaDetail(string detailName)
    {
        return detailName.Equals("5-hour quota", StringComparison.OrdinalIgnoreCase) ||
               detailName.Equals("Weekly quota", StringComparison.OrdinalIgnoreCase);
    }

    private void AddSubProviderCard(ProviderUsageDetail detail, StackPanel container)
    {
        // Compact sub-item (child provider detail)
        var grid = new Grid
        {
            Margin = new Thickness(20, 0, 0, 2),
            Height = 20,
            Background = Brushes.Transparent
        };

        // Calculate Percentages
        // Antigravity detail.Used comes as "80%" which represents REMAINING percentage
        double pctRemaining = 0;
        double pctUsed = 0;
        var hasPercent = false;

        // Try parse percentage
        var valueText = detail.Used?.Replace("%", "").Trim();
        if (double.TryParse(valueText, out double val))
        {
            hasPercent = true;
            pctRemaining = val; // Antigravity sends Remaining % in this field
            pctUsed = Math.Max(0, 100 - pctRemaining);
        }

        // Determine display values based on toggle
        bool showUsed = ShowUsedToggle?.IsChecked ?? false;
        double displayPct = showUsed ? pctUsed : pctRemaining;
        string displayStr = hasPercent
            ? $"{displayPct:F0}%"
            : (string.IsNullOrWhiteSpace(detail.Used) ? "Unknown" : detail.Used);

        // Calculate Bar Width (normalized to 0-100)
        double indicatorWidth = Math.Max(0, Math.Min(100, displayPct));

        // Background Progress Bar (Miniature)
        var pGrid = new Grid();
        pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indicatorWidth, GridUnitType.Star) });
        pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star) });

        var fill = new Border
        {
            Background = GetProgressBarColor(pctUsed), // Always color based on USED percentage
            Opacity = 0.3, // Slightly more transparent for sub-items
            CornerRadius = new CornerRadius(0)
        };
        pGrid.Children.Add(fill);
        if (hasPercent)
        {
            grid.Children.Add(pGrid);
        }

        // Content Overlay
        var bulletPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 6, 0) };

        var bullet = new Border
        {
            Width = 4, Height = 4,
            Background = GetResourceBrush("SecondaryText", Brushes.Gray),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(2, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        bulletPanel.Children.Add(bullet);
        DockPanel.SetDock(bullet, Dock.Left);

        // Reset time on the right (if available) - shown in yellow
        if (detail.NextResetTime.HasValue)
        {
            var resetBlock = new TextBlock
            {
                Text = $"({GetRelativeTimeString(detail.NextResetTime.Value)})",
                FontSize = 9,
                Foreground = GetResourceBrush("StatusTextWarning", Brushes.Goldenrod),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            bulletPanel.Children.Add(resetBlock);
            DockPanel.SetDock(resetBlock, Dock.Right);
        }

        // Value on the right
        var valueBlock = new TextBlock
        {
            Text = displayStr,
            FontSize = 10,
            Foreground = GetResourceBrush("TertiaryText", Brushes.Gray),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        bulletPanel.Children.Add(valueBlock);
        DockPanel.SetDock(valueBlock, Dock.Right);

        // Name on the left
        var nameBlock = new TextBlock
        {
            Text = detail.Name,
            FontSize = 10,
            Foreground = GetResourceBrush("SecondaryText", Brushes.LightGray),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        bulletPanel.Children.Add(nameBlock);
        DockPanel.SetDock(nameBlock, Dock.Left);

        grid.Children.Add(bulletPanel);
        container.Children.Add(grid);
    }

    private void AddCollapsibleSubProviders(ProviderUsage usage, StackPanel container)
    {
        if (usage.Details?.Any() != true) return;

        var displayableDetails = usage.Details
            .Where(IsDisplayableSubProviderDetail)
            .OrderBy(GetDetailDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!displayableDetails.Any())
        {
            return;
        }

        // Create collapsible section for sub-providers
        var (subHeader, subContainer) = CreateCollapsibleSubHeader(
            $"{usage.ProviderName} Details",
            Brushes.DeepSkyBlue,
            () => _preferences.IsAntigravityCollapsed,
            v => _preferences.IsAntigravityCollapsed = v);

        container.Children.Add(subHeader);
        container.Children.Add(subContainer);

        if (!_preferences.IsAntigravityCollapsed)
        {
            // Add sub-provider details
            foreach (var detail in displayableDetails)
            {
                AddSubProviderCard(detail, subContainer);
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

    private static string MaskAccountIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var atIndex = name.IndexOf('@');
        if (atIndex > 0 && atIndex < name.Length - 1)
        {
            var local = name[..atIndex];
            var domain = name[(atIndex + 1)..];
            var maskedDomainChars = domain.ToCharArray();
            for (var i = 0; i < maskedDomainChars.Length; i++)
            {
                if (maskedDomainChars[i] != '.')
                {
                    maskedDomainChars[i] = '*';
                }
            }

            var maskedDomain = new string(maskedDomainChars);
            if (local.Length <= 2)
            {
                return $"{new string('*', local.Length)}@{maskedDomain}";
            }

            return $"{local[0]}{new string('*', local.Length - 2)}{local[^1]}@{maskedDomain}";
        }

        if (name.Length <= 2) return new string('*', name.Length);
        return name[0] + new string('*', name.Length - 2) + name[^1];
    }

    private FrameworkElement CreateProviderIcon(string providerId)
    {
        var normalizedProviderId = providerId.StartsWith("antigravity.", StringComparison.OrdinalIgnoreCase)
            ? "antigravity"
            : providerId;

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
        string filename = normalizedProviderId.ToLower() switch
        {
            "github-copilot" => "github",
            "gemini-cli" => "google",
            "antigravity" => "google",
            "claude-code" or "claude" => "anthropic", // Use anthropic icon for claude
            "minimax" or "minimax-io" or "minimax-global" => "minimax",
            "kimi" => "kimi",
            "xiaomi" => "xiaomi",
            "zai" => "zai",
            "deepseek" => "deepseek",
            "openrouter" => "openai", // Fallback to openai
            "mistral" => "mistral",
            "openai" => "openai",
            "anthropic" => "anthropic",
            "google" => "google",
            "github" => "github",
            _ => normalizedProviderId.ToLower()
        };

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
        var (color, initial) = providerId.ToLower() switch
        {
            "openai" => (Brushes.DarkCyan, "AI"),
            "anthropic" => (Brushes.IndianRed, "An"),
            "github-copilot" => (Brushes.MediumPurple, "GH"),
            "gemini" or "google" or "antigravity" => (Brushes.DodgerBlue, "G"),
            "deepseek" => (Brushes.DeepSkyBlue, "DS"),
            "openrouter" => (Brushes.DarkSlateBlue, "OR"),
            "kimi" => (Brushes.MediumOrchid, "K"),
            "minimax" or "minimax-io" or "minimax-global" => (Brushes.DarkTurquoise, "MM"),
            "mistral" => (Brushes.OrangeRed, "Mi"),
            "xiaomi" => (Brushes.Orange, "Xi"),
            "zai" => (Brushes.LightSeaGreen, "Z"),
            "claude-code" or "claude" => (Brushes.Orange, "C"),
            "cloudcode" => (Brushes.DeepSkyBlue, "CC"),
            "codex" => (Brushes.MediumSeaGreen, "Cd"),
            "synthetic" => (Brushes.Gold, "Sy"),
            _ => (GetResourceBrush("SecondaryText", Brushes.Gray), providerId[..Math.Min(2, providerId.Length)].ToUpper())
        };

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
        _pollingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1) // Poll every minute
        };

        _pollingTimer.Tick += async (s, e) =>
        {
            // Poll agent every minute for fresh data
            try
            {
                var usages = await _agentService.GetUsageAsync();
                
                // Filter out placeholder data (safety filter - handles edge cases where bad data reaches UI)
                // Placeholder = no usage data AND not available
                var usableUsages = usages.Where(u => 
                    u.RequestsAvailable > 0 || u.RequestsUsed > 0 || u.IsAvailable
                ).ToList();
                
                if (usableUsages.Any())
                {
                    // Fresh data received - update UI
                    _usages = usableUsages;
                    RenderProviders();
                    await UpdateTrayIconsAsync();
                    _lastAgentUpdate = DateTime.Now;
                    ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                }
                else
                {
                    // Empty data - try to trigger a refresh if cooldown has passed
                    // This handles cases where Monitor restarted or hasn't completed its background refresh
                    var secondsSinceLastRefresh = (DateTime.Now - _lastRefreshTrigger).TotalSeconds;
                    if (secondsSinceLastRefresh >= RefreshCooldownSeconds)
                    {
                        Debug.WriteLine("Polling returned empty, triggering refresh...");
                        _lastRefreshTrigger = DateTime.Now;
                        try
                        {
                            await _agentService.TriggerRefreshAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Trigger refresh failed: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Polling returned empty, but refresh cooldown active ({secondsSinceLastRefresh:F0}s ago)");
                    }
                    
                    // Wait a moment and retry getting data
                    await Task.Delay(1000);
                    var retryUsages = await _agentService.GetUsageAsync();
                    
                    if (retryUsages.Any())
                    {
                        _usages = retryUsages.ToList();
                        RenderProviders();
                        await UpdateTrayIconsAsync();
                        _lastAgentUpdate = DateTime.Now;
                        ShowStatus($"{DateTime.Now:HH:mm:ss} (refreshed)", StatusType.Success);
                    }
                    else if (_usages.Any())
                    {
                        // Keep showing old data, show yellow warning
                        ShowStatus("Last update: " + _lastAgentUpdate.ToString("HH:mm:ss") + " (stale)", StatusType.Warning);
                    }
                    else
                    {
                        // No current data and no previous data - show warning
                        ShowStatus("No data - waiting for Monitor", StatusType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Polling error: {ex.Message}");
                if (_usages.Any())
                {
                    // Has old data - show yellow warning, keep displaying stale data
                    ShowStatus("Connection lost - showing stale data", StatusType.Warning);
                }
                else
                {
                    // No old data - show red error
                    ShowStatus("Connection error", StatusType.Error);
                }
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

        _configs = await _agentService.GetConfigsAsync();
        app.UpdateProviderTrayIcons(_usages, _configs, _preferences);
    }

    private void ShowStatus(string message, StatusType type)
    {
        if (type == StatusType.Success && !string.IsNullOrWhiteSpace(_agentContractWarningMessage))
        {
            message = _agentContractWarningMessage;
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
        var tooltipText = _lastAgentUpdate == DateTime.MinValue
            ? "Last update: Never"
            : $"Last update: {_lastAgentUpdate:HH:mm:ss}";

        if (StatusLed != null)
        {
            var ledToolTip = new ToolTip
            {
                Content = tooltipText,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                PlacementTarget = StatusLed
            };
            ledToolTip.Opened += (s, e) =>
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
            ledToolTip.Closed += (s, e) => _isTooltipOpen = false;
            StatusLed.ToolTip = ledToolTip;
        }
        if (StatusText != null)
        {
            var textToolTip = new ToolTip
            {
                Content = tooltipText,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                PlacementTarget = StatusText
            };
            textToolTip.Opened += (s, e) =>
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
            textToolTip.Closed += (s, e) => _isTooltipOpen = false;
            StatusText.ToolTip = textToolTip;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Debug.WriteLine($"[{timestamp}] [{type}] {message}");
        Console.WriteLine($"[{timestamp}] [{type}] {message}");
    }

    private void ApplyAgentContractStatus(AgentContractHandshakeResult handshakeResult)
    {
        if (handshakeResult.IsCompatible)
        {
            _agentContractWarningMessage = null;
            return;
        }

        _agentContractWarningMessage = handshakeResult.Message;
        ShowStatus(handshakeResult.Message, StatusType.Warning);
    }

    private void ShowErrorState(string message)
    {
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
        await RefreshDataAsync();
    }

    private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        await OpenSettingsDialogAsync();
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

    private void WebBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenWebUI();
    }

    private void OpenWebUI()
    {
        try
        {
            // Start the Web service if not running
            StartWebService();

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
            Debug.WriteLine($"Failed to open Web UI: {ex.Message}");
            MessageBox.Show($"Failed to open Web UI: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StartWebService()
    {
        try
        {
            // Check if web service is already running
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            try
            {
                var response = client.GetAsync("http://localhost:5100").GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("Web service already running");
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
                    Debug.WriteLine("Started Web service via dotnet run");
                    return;
                }

                Debug.WriteLine("Web executable not found");
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
            Debug.WriteLine($"Started Web service from: {webPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start Web service: {ex.Message}");
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
        var newPrivacyMode = !_isPrivacyMode;
        _preferences.IsPrivacyMode = newPrivacyMode;
        App.SetPrivacyMode(newPrivacyMode);
        await SaveUiPreferencesAsync();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private async void AlwaysOnTop_Checked(object sender, RoutedEventArgs e)
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

    private async void Compact_Checked(object sender, RoutedEventArgs e)
    {
       // No-op (Field removed from UI)
       await Task.CompletedTask;
    }

    private async void ShowUsedToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _preferences.InvertProgressBar = ShowUsedToggle.IsChecked ?? false;
        await SaveUiPreferencesAsync();

        // Refresh the display to show used% vs remaining%
        RenderProviders();
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
        if (!int.TryParse(prefix, out number))
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
            Debug.WriteLine($"Update check failed: {ex.Message}");
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


    private async Task RestartAgentAsync()
    {
        try
        {
            ShowStatus("Restarting monitor...", StatusType.Warning);

            // Try to start agent
            if (await MonitorLauncher.StartAgentAsync())
            {
                var agentReady = await MonitorLauncher.WaitForAgentAsync();
                if (agentReady)
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


    private async void AgentToggleBtn_Click(object sender, RoutedEventArgs e)
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
                UpdateAgentToggleButton(false);
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
                var agentReady = await MonitorLauncher.WaitForAgentAsync();
                if (agentReady)
                {
                    ShowStatus("Monitor started", StatusType.Success);
                    UpdateAgentToggleButton(true);
                    await RefreshDataAsync();
                }
                else
                {
                    ShowStatus("Monitor failed to start", StatusType.Error);
                    UpdateAgentToggleButton(false);
                }
            }
            else
            {
                ShowStatus("Could not start monitor", StatusType.Error);
                UpdateAgentToggleButton(false);
            }
        }
    }

    private void UpdateAgentToggleButton(bool isRunning)
    {
        if (AgentToggleBtn != null && AgentToggleIcon != null)
        {
            // Update icon: Play (E768) when stopped, Stop (E71A) when running
            AgentToggleIcon.Text = isRunning ? "\uE71A" : "\uE768";
            AgentToggleBtn.ToolTip = isRunning ? "Stop Monitor" : "Start Monitor";
        }
    }

    private async Task UpdateAgentToggleButtonStateAsync()
    {
        var (isRunning, _) = await MonitorLauncher.IsAgentRunningWithPortAsync();
        Dispatcher.Invoke(() => UpdateAgentToggleButton(isRunning));
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
