// <copyright file="MainWindow.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Updates;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.UI.Slim.Services;
using AIUsageTracker.UI.Slim.ViewModels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim;

public partial class MainWindow : Window
{
    private const int RefreshCooldownSeconds = 120;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private static readonly TimeSpan StartupPollingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NormalPollingInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TrayConfigRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);

    private readonly MainViewModel _viewModel;
    private readonly IMonitorService _monitorService;
    private readonly IMonitorLifecycleService _monitorLifecycleService;
    private readonly IMonitorStartupOrchestrator _monitorStartupOrchestrator;
    private readonly ILogger<MainWindow> _logger;
    private readonly IUpdateCheckerFactory _updateCheckerFactory;
    private readonly IDialogService _dialogService;
    private readonly IBrowserService _browserService;
    private readonly Func<string, FrameworkElement> _createProviderIcon;
    private readonly Func<string, FlowDocument> _buildChangelogDocument;
    private readonly UiPreferencesStore _preferencesStore;
    private readonly DisplayPreferencesService _displayPreferences;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly DispatcherTimer _alwaysOnTopTimer;

    private IUpdateCheckerService _updateChecker;
    private AppPreferences _preferences = new();
    private readonly object _dataLock = new();
    private List<ProviderUsage> _usages = new();
    private List<ProviderConfig> _configs = new();
    private bool _isPrivacyMode = App.IsPrivacyMode;
    private bool _isLoading;
    private DateTime _lastMonitorUpdate = DateTime.MinValue;
    private DateTime _lastRefreshTrigger = DateTime.MinValue;
    private bool _isPollingInProgress;
    private bool _isTrayIconUpdateInProgress;
    private DispatcherTimer? _pollingTimer;
    private DateTime _lastTrayConfigRefresh = DateTime.MinValue;
    private string? _monitorContractWarningMessage;
    private bool _isUpdateCheckInProgress;
    private HubConnection? _hubConnection;
    private HwndSource? _windowSource;
    private UpdateInfo? _latestUpdate;
    private bool _preferencesLoaded;
    private int _topmostRecoveryGeneration;
    private bool _isSettingsDialogOpen;
    private bool _isTooltipOpen;

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

    public MainWindow(
        MainViewModel viewModel,
        IMonitorService monitorService,
        IMonitorLifecycleService monitorLifecycleService,
        IMonitorStartupOrchestrator monitorStartupOrchestrator,
        ILogger<MainWindow> logger,
        IUpdateCheckerFactory updateCheckerFactory,
        IUpdateCheckerService updateChecker,
        IDialogService dialogService,
        IBrowserService browserService,
        IWpfProviderIconServiceFactory iconServiceFactory,
        IChangelogMarkdownRendererFactory markdownRendererFactory,
        UiPreferencesStore preferencesStore,
        DisplayPreferencesService displayPreferences)
        : this(
            skipUiInitialization: false,
            viewModel,
            monitorService,
            monitorLifecycleService,
            monitorStartupOrchestrator,
            logger,
            updateCheckerFactory,
            updateChecker,
            dialogService,
            browserService,
            iconServiceFactory,
            markdownRendererFactory,
            preferencesStore,
            displayPreferences)
    {
    }

    internal MainWindow(
        bool skipUiInitialization,
        MainViewModel viewModel,
        IMonitorService monitorService,
        IMonitorLifecycleService monitorLifecycleService,
        IMonitorStartupOrchestrator monitorStartupOrchestrator,
        ILogger<MainWindow> logger,
        IUpdateCheckerFactory updateCheckerFactory,
        IUpdateCheckerService updateChecker,
        IDialogService dialogService,
        IBrowserService browserService,
        IWpfProviderIconServiceFactory iconServiceFactory,
        IChangelogMarkdownRendererFactory markdownRendererFactory,
        UiPreferencesStore preferencesStore,
        DisplayPreferencesService displayPreferences)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(monitorLifecycleService);
        ArgumentNullException.ThrowIfNull(monitorStartupOrchestrator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(updateCheckerFactory);
        ArgumentNullException.ThrowIfNull(updateChecker);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(browserService);
        ArgumentNullException.ThrowIfNull(iconServiceFactory);
        ArgumentNullException.ThrowIfNull(markdownRendererFactory);
        ArgumentNullException.ThrowIfNull(preferencesStore);
        ArgumentNullException.ThrowIfNull(displayPreferences);

        if (!skipUiInitialization)
        {
            this.InitializeComponent();
            this.ApplyVersionDisplay();
        }

        this._logger = logger;
        this._monitorService = monitorService;
        this._monitorLifecycleService = monitorLifecycleService;
        this._monitorStartupOrchestrator = monitorStartupOrchestrator;
        this._updateCheckerFactory = updateCheckerFactory;
        this._updateChecker = updateChecker;
        this._dialogService = dialogService;
        this._browserService = browserService;
        this._createProviderIcon = iconServiceFactory.Create(this.GetResourceBrush);
        this._buildChangelogDocument = markdownRendererFactory.Create(this.GetResourceBrush);
        this._preferencesStore = preferencesStore;
        this._displayPreferences = displayPreferences;
        this._viewModel = viewModel;
        this.DataContext = this._viewModel;

        this._updateCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15),
        };
        this._alwaysOnTopTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };

        if (skipUiInitialization)
        {
            return;
        }

#pragma warning disable VSTHRD101 // WPF event subscriptions intentionally use async lambdas for UI event handlers.
        this._updateCheckTimer.Tick += async (s, e) =>
        {
            try
            {
                await this.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "UpdateCheckTimer_Tick failed");
            }
        };
        this._updateCheckTimer.Start();
        this._alwaysOnTopTimer.Tick += (s, e) => this.EnsureAlwaysOnTop();
        this._alwaysOnTopTimer.Start();

        this.SourceInitialized += this.OnSourceInitialized;
        PrivacyChangedWeakEventManager.AddHandler(this.OnPrivacyChanged);
        this.Closed += (s, e) =>
        {
            PrivacyChangedWeakEventManager.RemoveHandler(this.OnPrivacyChanged);
            this._updateCheckTimer.Stop();
            this._alwaysOnTopTimer.Stop();
            this.SourceInitialized -= this.OnSourceInitialized;

            if (this._hubConnection != null)
            {
                _ = this._hubConnection.DisposeAsync();
                this._hubConnection = null;
            }

            if (this._windowSource is not null)
            {
                this._windowSource.RemoveHook(this.WndProc);
                this._windowSource = null;
            }
        };
        this.UpdatePrivacyButtonState();

        this.Loaded += async (s, e) =>
        {
            try
            {
                await this.InitializeAsync();
                _ = this.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Window_Loaded failed");
            }
        };

        // Track window position changes
        this.LocationChanged += async (s, e) =>
        {
            try
            {
                await this.SaveWindowPositionAsync();
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "LocationChanged handler failed");
            }
        };
        this.SizeChanged += async (s, e) =>
        {
            try
            {
                await this.SaveWindowPositionAsync();
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "SizeChanged handler failed");
            }
        };
#pragma warning restore VSTHRD101
        this.Activated += (s, e) =>
        {
            this._topmostRecoveryGeneration++;
            this.EnsureAlwaysOnTop();
            this.LogWindowFocusTransition("Activated");
        };
        this.Deactivated += (s, e) =>
        {
            if (this._isSettingsDialogOpen)
            {
                this.LogWindowFocusTransition("Deactivated (settings open)");
                return;
            }

            var generation = ++this._topmostRecoveryGeneration;
            this.ScheduleTopmostRecovery(generation, TimeSpan.FromMilliseconds(250));
            this.ScheduleTopmostRecovery(generation, TimeSpan.FromSeconds(2));
            this.LogWindowFocusTransition("Deactivated");
        };
        this.StateChanged += (s, e) =>
        {
            if (this.WindowState == WindowState.Normal)
            {
                this.EnsureAlwaysOnTop();
            }

            this.LogWindowFocusTransition($"StateChanged -> {this.WindowState}");
        };
        this.IsVisibleChanged += (s, e) =>
        {
            if (this.IsVisible)
            {
                this.EnsureAlwaysOnTop();
            }

            this.LogWindowFocusTransition($"IsVisibleChanged -> {this.IsVisible}");
        };
    }

    private void ApplyVersionDisplay()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var appVersion = assembly.GetName().Version;
        var versionCore = appVersion != null
            ? $"{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}"
            : "0.0.0";

        var suffix = this.GetPrereleaseLabel(assembly);
        var displayVersion = string.IsNullOrWhiteSpace(suffix)
            ? $"v{versionCore}"
            : $"v{versionCore} {suffix}";

        this.VersionText.Text = displayVersion;
        this.Title = $"AI Usage Tracker {displayVersion}";
    }

    private string? GetPrereleaseLabel(Assembly assembly)
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

    private void PositionWindowNearTray()
    {
        // If saved position exists, use it
        if (this._preferences.WindowLeft.HasValue && this._preferences.WindowTop.HasValue)
        {
            // Ensure window is visible on screen
            var screen = SystemParameters.WorkArea;
            var left = Math.Max(screen.Left, Math.Min(this._preferences.WindowLeft.Value, screen.Right - this.Width));
            var top = Math.Max(screen.Top, Math.Min(this._preferences.WindowTop.Value, screen.Bottom - this.Height));

            this.Left = left;
            this.Top = top;
        }
    }

    private async Task SaveWindowPositionAsync()
    {
        if (!this.IsLoaded || !this._preferencesLoaded)
        {
            return;
        }

        // Only save if position has changed meaningfully
        var positionChanged =
            Math.Abs(this._preferences.WindowLeft.GetValueOrDefault() - this.Left) > 1 ||
            Math.Abs(this._preferences.WindowTop.GetValueOrDefault() - this.Top) > 1;

        var sizeChanged =
            Math.Abs(this._preferences.WindowWidth - this.Width) > 1 ||
            Math.Abs(this._preferences.WindowHeight - this.Height) > 1;

        if (positionChanged || sizeChanged)
        {
            this._preferences.WindowLeft = this.Left;
            this._preferences.WindowTop = this.Top;
            this._preferences.WindowWidth = this.Width;
            this._preferences.WindowHeight = this.Height;
            await this.SaveUiPreferencesAsync();
        }
    }

    private async Task InitializeAsync()
    {
        if (this._isLoading)
        {
            return;
        }

        try
        {
            this._isLoading = true;

            if (!this._preferencesLoaded)
            {
                this._preferences = await this._preferencesStore.LoadAsync();
                App.Preferences = this._preferences;
                this._isPrivacyMode = this._preferences.IsPrivacyMode;
                App.SetPrivacyMode(this._isPrivacyMode);
                this._preferencesLoaded = true;
                this.ApplyPreferences();
                this.PositionWindowNearTray();
            }

            this.ShowStatus("Checking monitor status...", StatusType.Info);

            var startupResult = await this._monitorStartupOrchestrator.EnsureMonitorReadyAsync(
                async (message, type) =>
                {
                    await this.Dispatcher.InvokeAsync(() => this.ShowStatus(message, type));
                });
            var success = startupResult.IsSuccess;

            if (!success && startupResult.IsLaunchFailure)
            {
                this.ShowStatus("Monitor failed to start", StatusType.Error);
                this.ShowErrorState(
                    MonitorErrorPresentationCatalog.BuildLaunchErrorMessage(
                        this._monitorService.LastAgentErrors));
            }

            if (success)
            {
                await this.UpdateMonitorToggleButtonStateAsync();
                var handshakeResult = await this._monitorService.CheckApiContractAsync();
                this.ApplyMonitorContractStatus(handshakeResult);

                // Rapid polling at startup until data is available
                await this.RapidPollUntilDataAvailableAsync();

                // Start polling timer - UI polls Agent every minute
                this.StartPollingTimer();

                // Initialize SignalR connection for push updates
                _ = this.InitializeSignalRAsync();

                this.ShowStatus("Connected", StatusType.Success);
            }
            else
            {
                // Ensure UI shows an error state if background initialization failed
                // and no error message was shown (e.g., due to unhandled exception in Task.Run)
                bool hasUsages;
                lock (this._dataLock)
                {
                    hasUsages = this._usages.Any();
                }

                if (MonitorStartupPresentationCatalog.ShouldShowConnectionFailureState(
                        hasUsages,
                        this.ProvidersList.Children.Count))
                {
                    // Still showing default "Loading..." - update to error state
                    this.ShowErrorState("Failed to connect to Monitor. Try refreshing.");
                }
            }
        }
        catch (Exception ex)
        {
            this.ShowErrorState($"Initialization failed: {ex.Message}");
        }
        finally
        {
            this._isLoading = false;
        }
    }

    private async Task RapidPollUntilDataAvailableAsync()
    {
        const int maxAttempts = 15;
        const int pollIntervalMs = 2000; // 2 seconds between attempts

        this.LogDiagnostic("[DIAGNOSTIC] RapidPollUntilDataAvailableAsync starting...");
        this.ShowStatus("Loading data...", StatusType.Info);

        // First, check if Monitor is reachable
        this.LogDiagnostic("[DIAGNOSTIC] Checking Monitor health...");
        var isHealthy = await this._monitorService.CheckHealthAsync();
        this.LogDiagnostic($"[DIAGNOSTIC] Monitor health: {isHealthy}");

        if (!isHealthy)
        {
            await this._monitorService.RefreshAgentInfoAsync();
            this.ShowStatus("Monitor not reachable", StatusType.Error);
            this.ShowErrorState(
                MonitorErrorPresentationCatalog.BuildConnectionErrorMessage(
                    this._monitorService.LastAgentErrors));
            return;
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            this.LogDiagnostic($"[DIAGNOSTIC] Poll attempt {attempt + 1}/{maxAttempts}");

            try
            {
                // Try to get cached data from monitor
                this.LogDiagnostic("[DIAGNOSTIC] Calling GetUsageAsync...");
                var usages = await this.GetUsageForDisplayAsync();
                this.LogDiagnostic($"[DIAGNOSTIC] GetUsageAsync returned {usages.Count} providers");

                // Show all providers from monitor (filtering already done in database)
                if (usages.Any())
                {
                    this.LogDiagnostic("[DIAGNOSTIC] Data available, rendering...");

                    // Data is available - render and stop rapid polling
                    lock (this._dataLock)
                    {
                        this._usages = usages.ToList();
                    }

                    this.RenderProviders();
                    this._lastMonitorUpdate = DateTime.Now;
                    this.ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                    _ = this.UpdateTrayIconsAsync();
                    this.LogDiagnostic("[DIAGNOSTIC] Data rendered successfully");
                    return;
                }

                this.LogDiagnostic("[DIAGNOSTIC] No data available");

                // No data yet - on first attempt, trigger a background refresh
                // and keep polling so data appears as soon as refresh completes.
                if (attempt == 0)
                {
                    this.LogDiagnostic("[DIAGNOSTIC] First attempt, no data - triggering background refresh...");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await this._monitorService.TriggerRefreshAsync();
                            this.LogDiagnostic("[DIAGNOSTIC] Background refresh triggered");
                        }
                        catch (Exception ex)
                        {
                            this.LogDiagnostic($"[DIAGNOSTIC] Background refresh failed: {ex.Message}");
                        }
                    });

                    // Show UI immediately with empty state
                    this.LogDiagnostic("[DIAGNOSTIC] Showing empty state...");
                    this.ShowStatus("Scanning for providers...", StatusType.Info);
                    this.LogDiagnostic("[DIAGNOSTIC] About to call RenderProviders...");
                    this.RenderProviders(); // Will show empty or loading state
                    this.LogDiagnostic("[DIAGNOSTIC] RenderProviders completed");
                }

                // No data yet - wait and try again
                if (attempt < maxAttempts - 1)
                {
                    this.ShowStatus($"Waiting for data... ({attempt + 1}/{maxAttempts})", StatusType.Warning);
                    await Task.Delay(pollIntervalMs);
                }
            }
            catch (HttpRequestException ex)
            {
                this.LogDiagnostic($"[DIAGNOSTIC] Connection error: {ex.Message}");
                this.ShowStatus("Connection lost", StatusType.Error);
                this.ShowErrorState($"Lost connection to Monitor:\n{ex.Message}\n\nTry refreshing or restarting the Monitor.");
                return;
            }
            catch (Exception ex)
            {
                this.LogDiagnostic($"[DIAGNOSTIC] Error: {ex.Message}");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(pollIntervalMs);
                }
            }
        }

        this.LogDiagnostic("[DIAGNOSTIC] Max attempts reached, no data available");
        this.ShowStatus("No data available", StatusType.Error);
        this.ShowErrorState("No provider data available.\n\nThe Monitor may still be initializing.\nTry refreshing manually or check Settings > Monitor.");
    }

    private void ApplyPreferences()
    {
        // Apply window settings
        this.Topmost = this._preferences.AlwaysOnTop;
        this.Width = this._preferences.WindowWidth;
        this.Height = this._preferences.WindowHeight;

        // Note: PositionWindowNearTray() is only called on initial load, not here
        // to prevent window from jumping when closing settings dialog
        if (!string.IsNullOrWhiteSpace(this._preferences.FontFamily))
        {
            this.FontFamily = new FontFamily(this._preferences.FontFamily);
        }

        if (this._preferences.FontSize > 0)
        {
            this.FontSize = this._preferences.FontSize;
        }

        this.FontWeight = this._preferences.FontBold ? FontWeights.Bold : FontWeights.Normal;
        this.FontStyle = this._preferences.FontItalic ? FontStyles.Italic : FontStyles.Normal;

        // Apply UI controls
        this.AlwaysOnTopCheck.IsChecked = this._preferences.AlwaysOnTop;
        this.ApplyDisplayModePreference();
        this.UpdatePrivacyButtonState();
        this.EnsureAlwaysOnTop();

        // Reinitialize update checker with correct channel
        this.InitializeUpdateChecker();
    }

    private void ApplyDisplayModePreference()
    {
        if (this.ShowUsedToggle != null)
        {
            this.ShowUsedToggle.IsChecked = this._displayPreferences.ShouldShowUsedPercentages(this._preferences);
        }
    }

    private void InitializeUpdateChecker()
    {
        if (this._preferences == null)
        {
            return;
        }

        var channel = this._preferences.UpdateChannel;
        this._updateChecker = this._updateCheckerFactory.Create(channel);
    }

    private async Task SaveUiPreferencesAsync()
    {
        App.Preferences = this._preferences;
        var saved = await this._preferencesStore.SaveAsync(this._preferences);
        if (!saved)
        {
            this._logger.LogWarning("Failed to save Slim UI preferences");
        }
    }

    internal void ShowAndActivate()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
        this.EnsureAlwaysOnTop();
    }

    internal async Task PrepareForHeadlessScreenshotAsync(bool deterministic = false)
    {
        if (deterministic)
        {
            var fixture = MainWindowDeterministicFixture.Create();
            this._preferences = fixture.Preferences;

            App.Preferences = this._preferences;
            this._isPrivacyMode = true;
            App.SetPrivacyMode(true);
            this._preferencesLoaded = true;
            this._lastMonitorUpdate = fixture.LastMonitorUpdate;
            this.ApplyPreferences();
            this.Width = fixture.WindowWidth;
            this.Height = this.MinHeight;
            this._usages = fixture.Usages;

            this.RenderProviders();
            this.ShowStatus($"{this._lastMonitorUpdate:HH:mm:ss}", StatusType.Success);
        }
        else
        {
            await this.InitializeAsync();
        }
    }

    private void FitWindowHeightForHeadlessScreenshot()
    {
        if (this.Content is not FrameworkElement root)
        {
            return;
        }

        var width = this.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = this.ActualWidth > 0 ? this.ActualWidth : 460;
        }

        root.Measure(new Size(width, double.PositiveInfinity));
        var desiredHeight = Math.Ceiling(root.DesiredSize.Height);
        if (desiredHeight > 0)
        {
            this.Height = Math.Max(this.MinHeight, desiredHeight);
        }

        this.UpdateLayout();

        if (this.ProvidersScrollViewer is null)
        {
            return;
        }

        var overflow = this.ProvidersScrollViewer.ExtentHeight - this.ProvidersScrollViewer.ViewportHeight;
        if (overflow > 0.5)
        {
            this.Height += Math.Ceiling(overflow) + 2;
            this.UpdateLayout();
        }
    }

    private void OnPrivacyChanged(object? sender, PrivacyChangedEventArgs e)
    {
        if (!this.Dispatcher.CheckAccess())
        {
            _ = this.Dispatcher.BeginInvoke(new Action(() => this.OnPrivacyChanged(sender, e)));
            return;
        }

        this._isPrivacyMode = e.IsPrivacyMode;
        this._preferences.IsPrivacyMode = e.IsPrivacyMode;
        this.UpdatePrivacyButtonState();

        bool hasUsages;
        lock (this._dataLock)
        {
            hasUsages = this._usages.Count > 0;
        }

        if (hasUsages)
        {
            this.RenderProviders();
        }
    }

    private void UpdatePrivacyButtonState()
    {
        if (this.PrivacyBtn == null)
        {
            return;
        }

        this.PrivacyBtn.Content = this._isPrivacyMode ? "\uE72E" : "\uE785";
        this.PrivacyBtn.Foreground = this._isPrivacyMode
            ? Brushes.Gold
            : (this.TryFindResource("SecondaryText") as Brush ?? Brushes.Gray);
    }

    private async Task RefreshDataAsync()
    {
        if (this._isLoading)
        {
            return;
        }

        try
        {
            this._isLoading = true;
            this.ShowStatus("Refreshing...", StatusType.Info);

            // Trigger refresh on monitor
            await this._monitorService.TriggerRefreshAsync();

            // Get updated usage data
            var latestUsages = await this.GetUsageForDisplayAsync();
            var now = DateTime.Now;
            var hasLatestUsages = latestUsages.Any();
            bool hasCurrentUsages = false;
            if (!hasLatestUsages)
            {
                lock (this._dataLock)
                {
                    hasCurrentUsages = this._usages.Any();
                }
            }

            var refreshPresentation = RefreshDataPresentationCatalog.Create(
                hasLatestUsages: hasLatestUsages,
                hasCurrentUsages: hasCurrentUsages,
                now: now);

            if (refreshPresentation.ApplyLatestUsages)
            {
                lock (this._dataLock)
                {
                    this._usages = latestUsages.ToList();
                }

                this.RenderProviders();
            }

            if (refreshPresentation.UpdateLastMonitorTimestamp)
            {
                this._lastMonitorUpdate = now;
            }

            if (refreshPresentation.StatusMessage != null && refreshPresentation.StatusType.HasValue)
            {
                this.ShowStatus(refreshPresentation.StatusMessage, refreshPresentation.StatusType.Value);
            }

            if (refreshPresentation.TriggerTrayIconUpdate)
            {
                _ = this.UpdateTrayIconsAsync();
            }

            if (refreshPresentation.UseErrorState && refreshPresentation.ErrorStateMessage != null)
            {
                this.ShowErrorState(refreshPresentation.ErrorStateMessage);
            }
        }
        catch (Exception ex)
        {
            this.ShowErrorState($"Refresh failed: {ex.Message}");
        }
        finally
        {
            this._isLoading = false;
        }
    }

    private async Task<IReadOnlyList<ProviderUsage>> GetUsageForDisplayAsync()
    {
        var groupedSnapshot = await this._monitorService.GetGroupedUsageAsync();
        if (groupedSnapshot == null)
        {
            this._logger.LogWarning("Grouped usage snapshot is unavailable.");
            return Array.Empty<ProviderUsage>();
        }

        return GroupedUsageDisplayAdapter.Expand(groupedSnapshot);
    }

    // UI Element Creation Helpers
    private TextBlock CreateText(
        string text,
        double fontSize,
        Brush foreground,
        FontWeight? fontWeight = null,
        Thickness? margin = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground,
            FontWeight = fontWeight ?? FontWeights.Normal,
            Margin = margin ?? new Thickness(0),
        };
    }

    private Border CreateSeparator(Brush color, double opacity = 0.5, double height = 1)
    {
        return new Border
        {
            Height = height,
            Background = color,
            Opacity = opacity,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private Grid CreateCollapsibleHeaderGrid(Thickness margin)
    {
        var header = new Grid { Margin = margin };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return header;
    }

    private SolidColorBrush GetResourceBrush(string key, SolidColorBrush fallback)
    {
        return this.FindResource(key) as SolidColorBrush ?? fallback;
    }

    private void RenderProviders()
    {
        this.LogDiagnostic("[DIAGNOSTIC] RenderProviders called");
        this.ProvidersList.Children.Clear();

        List<ProviderUsage> usagesCopy;
        lock (this._dataLock)
        {
            usagesCopy = this._usages?.ToList() ?? new List<ProviderUsage>();
        }

        this.LogDiagnostic($"[DIAGNOSTIC] ProvidersList cleared, _usages count: {usagesCopy.Count}");
        var renderPlan = ProviderRenderPlanCatalog.Build(usagesCopy, this._preferences.HiddenProviderItemIds);
        this.LogDiagnostic(
            $"[DIAGNOSTIC] Provider render counts: raw={renderPlan.RawCount}, rendered={renderPlan.RenderedCount}");

        if (!string.IsNullOrWhiteSpace(renderPlan.Message))
        {
            this.LogDiagnostic($"[DIAGNOSTIC] {renderPlan.Message}");
            try
            {
                var messageBlock = this.CreateInfoTextBlock(renderPlan.Message);
                this.ProvidersList.Children.Add(messageBlock);
                this.ApplyProviderListFontPreferences();
                this.LogDiagnostic("[DIAGNOSTIC] Empty-state provider message added");
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to add empty-state provider message");
            }

            return;
        }

        try
        {
            this.LogDiagnostic($"[DIAGNOSTIC] Rendering {renderPlan.RenderedCount} providers...");

            var cardRenderer = this.CreateProviderCardRenderer();
            foreach (var section in renderPlan.Sections)
            {
                var (header, container) = this.CreateCollapsibleHeader(
                    section.Title,
                    section.IsQuotaBased ? Brushes.DeepSkyBlue : Brushes.MediumSeaGreen,
                    isGroupHeader: true,
                    groupKey: section.SectionKey,
                    () => ProviderSectionCollapseCatalog.GetIsCollapsed(this._preferences, section.IsQuotaBased),
                    v => ProviderSectionCollapseCatalog.SetIsCollapsed(this._preferences, section.IsQuotaBased, v));

                this.ProvidersList.Children.Add(header);
                this.ProvidersList.Children.Add(container);

                var isCollapsed = ProviderSectionCollapseCatalog.GetIsCollapsed(this._preferences, section.IsQuotaBased);
                if (isCollapsed)
                {
                    continue;
                }

                foreach (var usage in section.Usages)
                {
                    this.AddProviderCard(usage, container, cardRenderer);

                    if (usage.Details?.Any() == true)
                    {
                        this.AddCollapsibleSubProviders(usage, container, cardRenderer);
                    }
                }
            }

            this.ApplyProviderListFontPreferences();
        }
        catch (Exception ex)
        {
            this.LogDiagnostic($"[DIAGNOSTIC] RenderProviders failed: {ex}");
            this.ProvidersList.Children.Clear();
            this.ProvidersList.Children.Add(this.CreateInfoTextBlock("Failed to render provider cards. Check logs for details."));
            this.ApplyProviderListFontPreferences();
        }
    }

    private void ApplyProviderListFontPreferences()
    {
        if (this.ProvidersList == null)
        {
            return;
        }

        this.ApplyFontPreferencesToElement(this.ProvidersList);
    }

    private void ApplyFontPreferencesToElement(DependencyObject element)
    {
        if (element is TextBlock textBlock)
        {
            if (!string.IsNullOrWhiteSpace(this._preferences.FontFamily))
            {
                textBlock.FontFamily = new FontFamily(this._preferences.FontFamily);
            }

            if (this._preferences.FontSize > 0)
            {
                textBlock.FontSize = Math.Max(8, textBlock.FontSize * (this._preferences.FontSize / 12.0));
            }

            if (this._preferences.FontBold)
            {
                textBlock.FontWeight = FontWeights.Bold;
            }

            if (this._preferences.FontItalic)
            {
                textBlock.FontStyle = FontStyles.Italic;
            }
        }

        switch (element)
        {
            case Panel panel:
                foreach (UIElement child in panel.Children)
                {
                    this.ApplyFontPreferencesToElement(child);
                }

                break;

            case Border border when border.Child is not null:
                this.ApplyFontPreferencesToElement(border.Child);
                break;

            case Decorator decorator when decorator.Child is not null:
                this.ApplyFontPreferencesToElement(decorator.Child);
                break;

            case ContentControl contentControl when contentControl.Content is DependencyObject child:
                this.ApplyFontPreferencesToElement(child);
                break;
        }
    }

    private (UIElement Header, StackPanel Container) CreateCollapsibleHeader(
        string title,
        Brush accent,
        bool isGroupHeader,
        string? groupKey,
        Func<bool> getCollapsed,
        Action<bool> setCollapsed)
    {
        var presentation = CollapsibleHeaderPresentationCatalog.Create(title, isGroupHeader);
        var titleForeground = presentation.UseAccentForTitle
            ? accent
            : this.GetResourceBrush("SecondaryText", Brushes.Gray);

        var header = this.CreateCollapsibleHeaderGrid(presentation.Margin);

        // Toggle button
        var toggleText = this.CreateText(
            getCollapsed() ? "▶" : "▼",
            presentation.ToggleFontSize,
            accent,
            FontWeights.Bold,
            new Thickness(0, 0, 5, 0));
        toggleText.VerticalAlignment = VerticalAlignment.Center;
        toggleText.Opacity = presentation.ToggleOpacity;
        toggleText.Tag = "ToggleIcon";

        // Title
        var titleBlock = this.CreateText(
            presentation.TitleText,
            10.0,
            titleForeground,
            presentation.TitleFontWeight,
            new Thickness(0, 0, 10, 0));
        titleBlock.VerticalAlignment = VerticalAlignment.Center;

        // Separator line
        var line = this.CreateSeparator(accent, presentation.LineOpacity);

        // Container
        var container = new StackPanel();
        if (!string.IsNullOrEmpty(groupKey))
        {
            container.Tag = $"{groupKey}Container";
        }

        container.Visibility = getCollapsed() ? Visibility.Collapsed : Visibility.Visible;

        // Click handler
        header.Cursor = System.Windows.Input.Cursors.Hand;
        header.MouseLeftButtonDown += async (s, e) =>
        {
            var newState = !getCollapsed();
            setCollapsed(newState);
            container.Visibility = newState ? Visibility.Collapsed : Visibility.Visible;
            toggleText.Text = newState ? "▶" : "▼";
            await this.SaveUiPreferencesAsync();
        };

        Grid.SetColumn(toggleText, 0);
        Grid.SetColumn(titleBlock, 1);
        Grid.SetColumn(line, 2);

        header.Children.Add(toggleText);
        header.Children.Add(titleBlock);
        header.Children.Add(line);

        return (header, container);
    }

    private ProviderCardRenderer CreateProviderCardRenderer()
    {
        return new ProviderCardRenderer(
            this._preferences,
            this._isPrivacyMode,
            this.GetResourceBrush,
            this._createProviderIcon,
            this.CreateTopmostAwareToolTip,
            this.ConfigureCardToolTip,
            nextReset => RelativeTimePresentationCatalog.FormatUntil(nextReset, DateTime.Now));
    }

    private void AddProviderCard(ProviderUsage usage, StackPanel container, ProviderCardRenderer cardRenderer, bool isChild = false)
    {
        var showUsed = this.ShowUsedToggle?.IsChecked ?? false;
        var card = cardRenderer.CreateProviderCard(usage, showUsed, isChild);
        container.Children.Add(card);
    }

    private ToolTip CreateTopmostAwareToolTip(FrameworkElement placementTarget, object content)
    {
        var toolTip = new ToolTip
        {
            Content = content,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            PlacementTarget = placementTarget,
        };

        toolTip.Opened += (s, e) =>
        {
            this._isTooltipOpen = true;
            if (s is ToolTip tip && tip.PlacementTarget != null)
            {
                var tooltipWindow = Window.GetWindow(tip);
                if (tooltipWindow != null && this.Topmost)
                {
                    tooltipWindow.Topmost = true;
                }
            }
        };
        toolTip.Closed += (s, e) => this._isTooltipOpen = false;

        return toolTip;
    }

    private void ConfigureCardToolTip(FrameworkElement target)
    {
        ToolTipService.SetInitialShowDelay(target, 100);
        ToolTipService.SetShowDuration(target, 15000);
    }


    private void AddSubProviderCard(ProviderUsage usage, ProviderUsageDetail detail, StackPanel container, ProviderCardRenderer cardRenderer)
    {
        var showUsed = this.ShowUsedToggle?.IsChecked ?? false;
        var subCard = cardRenderer.CreateSubProviderCard(usage, detail, showUsed);
        container.Children.Add(subCard);
    }

    private void AddCollapsibleSubProviders(ProviderUsage usage, StackPanel container, ProviderCardRenderer cardRenderer)
    {
        var section = ProviderSubDetailSectionCatalog.Build(usage, this._preferences);
        if (section is null)
        {
            return;
        }

        var (subHeader, subContainer) = this.CreateCollapsibleHeader(
            section.Title,
            Brushes.DeepSkyBlue,
            isGroupHeader: false,
            groupKey: null,
            () => ProviderSubDetailSectionCatalog.GetIsCollapsed(this._preferences, section.ProviderId),
            v => ProviderSubDetailSectionCatalog.SetIsCollapsed(this._preferences, section.ProviderId, v));

        container.Children.Add(subHeader);
        container.Children.Add(subContainer);

        if (section.IsCollapsed)
        {
            return;
        }

        foreach (var detail in section.Details)
        {
            this.AddSubProviderCard(usage, detail, subContainer, cardRenderer);
        }
    }

    private void StartPollingTimer()
    {
        this._pollingTimer?.Stop();

        bool hasUsages;
        lock (this._dataLock)
        {
            hasUsages = this._usages.Any();
        }

        this._pollingTimer = new DispatcherTimer
        {
            Interval = PollingPresentationCatalog.ResolveInitialInterval(
                hasUsages,
                StartupPollingInterval,
                NormalPollingInterval),
        };

        this._pollingTimer.Tick += async (s, e) =>
        {
            if (this._isPollingInProgress)
            {
                return;
            }

            // Poll monitor for fresh data
            try
            {
                var usages = await this.GetUsageForDisplayAsync();

                // Show all providers from monitor (filtering already done in database)
                if (usages.Any())
                {
                    // Fresh data received - update UI
                    await this.Dispatcher.InvokeAsync(async () =>
                    {
                        await this.FetchDataAsync();
                    });
                }
                else
                {
                    // Empty data - try to trigger a refresh if cooldown has passed
                    // This handles cases where Monitor restarted or hasn't completed its background refresh
                    var refreshDecision = PollingRefreshDecisionCatalog.Create(
                        this._lastRefreshTrigger,
                        DateTime.Now,
                        RefreshCooldownSeconds);
                    if (refreshDecision.ShouldTriggerRefresh)
                    {
                        this._logger.LogDebug("Polling returned empty, triggering refresh");
                        this._lastRefreshTrigger = DateTime.Now;
                        try
                        {
                            await this._monitorService.TriggerRefreshAsync();
                        }
                        catch (Exception ex)
                        {
                            this._logger.LogWarning(ex, "TriggerRefreshAsync failed during polling retry");
                        }
                    }
                    else
                    {
                        this._logger.LogDebug(
                            "Polling returned empty, refresh cooldown active ({SecondsSinceLastRefresh:F0}s ago)",
                            refreshDecision.SecondsSinceLastRefresh);
                    }

                    // Wait a moment and retry getting data
                    await Task.Delay(1000);
                    await this.Dispatcher.InvokeAsync(async () =>
                    {
                        await this.FetchDataAsync(" (refreshed)");
                    });

                    bool hasCurrentUsages;
                    lock (this._dataLock)
                    {
                        hasCurrentUsages = this._usages.Any();
                    }

                    var noDataPresentation = PollingPresentationCatalog.ResolveAfterEmptyRetry(
                        hasCurrentUsages,
                        this._lastMonitorUpdate,
                        DateTime.Now);
                    if (noDataPresentation.Message != null && noDataPresentation.StatusType.HasValue)
                    {
                        this.ShowStatus(noDataPresentation.Message, noDataPresentation.StatusType.Value);
                    }

                    if (noDataPresentation.SwitchToStartupInterval &&
                        this._pollingTimer != null &&
                        this._pollingTimer.Interval != StartupPollingInterval)
                    {
                        this._pollingTimer.Interval = StartupPollingInterval;
                    }
                }
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Polling loop error");
                bool hasOldData;
                lock (this._dataLock)
                {
                    hasOldData = this._usages.Any();
                }

                var exceptionPresentation = PollingPresentationCatalog.ResolveOnPollingException(
                    hasOldData,
                    this._lastMonitorUpdate,
                    DateTime.Now);
                if (exceptionPresentation.Message != null && exceptionPresentation.StatusType.HasValue)
                {
                    this.ShowStatus(exceptionPresentation.Message, exceptionPresentation.StatusType.Value);
                }

                if (exceptionPresentation.SwitchToStartupInterval &&
                    this._pollingTimer != null &&
                    this._pollingTimer.Interval != StartupPollingInterval)
                {
                    this._pollingTimer.Interval = StartupPollingInterval;
                }
            }
        };

        this._pollingTimer.Start();
    }

    private async Task UpdateTrayIconsAsync()
    {
        if (Application.Current is not App app)
        {
            return;
        }

        if (this._isTrayIconUpdateInProgress)
        {
            return;
        }

        this._isTrayIconUpdateInProgress = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            bool hasCachedConfigs;
            lock (this._dataLock)
            {
                hasCachedConfigs = this._configs.Any();
            }

            var shouldRefreshConfigs = TrayConfigRefreshDecisionCatalog.ShouldRefresh(
                hasCachedConfigs: hasCachedConfigs,
                lastRefreshUtc: this._lastTrayConfigRefresh,
                nowUtc: DateTime.UtcNow,
                refreshInterval: TrayConfigRefreshInterval);

            if (shouldRefreshConfigs)
            {
                var configs = (await this._monitorService.GetConfigsAsync().ConfigureAwait(true)).ToList();
                lock (this._dataLock)
                {
                    this._configs = configs;
                }

                this._lastTrayConfigRefresh = DateTime.UtcNow;
            }

            List<ProviderUsage> usagesCopy;
            List<ProviderConfig> configsCopy;
            lock (this._dataLock)
            {
                usagesCopy = this._usages.ToList();
                configsCopy = this._configs.ToList();
            }

            app.UpdateProviderTrayIcons(usagesCopy, configsCopy, this._preferences);
        }
        catch (Exception ex)
        {
            this.LogDiagnostic($"[DIAGNOSTIC] UpdateTrayIconsAsync failed: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            this.LogDiagnostic($"[DIAGNOSTIC] UpdateTrayIconsAsync completed in {stopwatch.ElapsedMilliseconds}ms");
            this._isTrayIconUpdateInProgress = false;
        }
    }

    private async Task InitializeSignalRAsync()
    {
        try
        {
            var hubUrl = $"{this._monitorService.AgentUrl.TrimEnd('/')}/hubs/usage";
            this._logger.LogInformation("Initializing SignalR connection to {HubUrl}", hubUrl);

            this._hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            this._hubConnection.On("RefreshStarted", async () =>
            {
                await this.Dispatcher.InvokeAsync(() =>
                {
                    this.ShowStatus("Monitor refreshing...", StatusType.Info);
                });
            });

            this._hubConnection.On("UsageUpdated", async () =>
            {
                this._logger.LogInformation("SignalR: Received UsageUpdated event");
                await this.Dispatcher.InvokeAsync(async () =>
                {
                    await this.FetchDataAsync(" (real-time)");
                });
            });

            await this._hubConnection.StartAsync();
            this._logger.LogInformation("SignalR connection established");
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to initialize SignalR connection. Falling back to polling only.");
        }
    }

    private async Task FetchDataAsync(string statusSuffix = "")
    {
        if (this._isPollingInProgress)
        {
            return;
        }

        this._isPollingInProgress = true;
        try
        {
            var usages = await this.GetUsageForDisplayAsync();
            if (usages.Any())
            {
                var now = DateTime.Now;
                var successPresentation = FetchDataSuccessPresentationCatalog.Create(
                    now: now,
                    statusSuffix: statusSuffix,
                    hasPollingTimer: this._pollingTimer != null,
                    currentInterval: this._pollingTimer?.Interval ?? TimeSpan.Zero,
                    normalInterval: NormalPollingInterval);

                lock (this._dataLock)
                {
                    this._usages = usages.ToList();
                }

                this.RenderProviders();
                this._lastMonitorUpdate = now;
                this.ShowStatus(successPresentation.StatusMessage, StatusType.Success);
                _ = this.UpdateTrayIconsAsync();

                if (successPresentation.SwitchToNormalInterval && this._pollingTimer != null)
                {
                    this._pollingTimer.Interval = NormalPollingInterval;
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "FetchDataAsync failed");
        }
        finally
        {
            this._isPollingInProgress = false;
        }
    }

    private string FormatMonitorOfflineStatus()
    {
        return MonitorOfflineStatusCatalog.Format(this._lastMonitorUpdate, DateTime.Now);
    }

    private void LogDiagnostic(string message)
    {
        this._logger.LogInformation("{DiagnosticMessage}", message);
        UiDiagnosticFileLog.Write(message);
    }

    private void ShowStatus(string message, StatusType type)
    {
        var presentation = StatusPresentationCatalog.Create(
            message,
            type,
            this._monitorContractWarningMessage,
            this._lastMonitorUpdate);

        if (this.StatusText != null)
        {
            this.StatusText.Text = presentation.Message;
        }

        // Update LED color
        if (this.StatusLed != null)
        {
            this.StatusLed.Fill = presentation.IndicatorKind switch
            {
                StatusIndicatorKind.Success => this.GetResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen),
                StatusIndicatorKind.Warning => Brushes.Gold,
                StatusIndicatorKind.Error => this.GetResourceBrush("ProgressBarRed", Brushes.Crimson),
                _ => this.GetResourceBrush("SecondaryText", Brushes.Gray),
            };
        }

        if (this.StatusLed != null)
        {
            this.StatusLed.ToolTip = this.CreateTopmostAwareToolTip(this.StatusLed, presentation.TooltipText);
        }

        if (this.StatusText != null)
        {
            this.StatusText.ToolTip = this.CreateTopmostAwareToolTip(this.StatusText, presentation.TooltipText);
        }

        this._logger.Log(
            presentation.LogLevel,
            "[{StatusType}] {StatusMessage}",
            presentation.Type,
            presentation.Message);
    }

    private void ApplyMonitorContractStatus(AgentContractHandshakeResult handshakeResult)
    {
        if (handshakeResult.IsCompatible)
        {
            this._monitorContractWarningMessage = null;
            return;
        }

        this._monitorContractWarningMessage = handshakeResult.Message;
        this.ShowStatus(handshakeResult.Message, StatusType.Warning);
    }

    private void ShowErrorState(string message)
    {
        bool hasUsages;
        lock (this._dataLock)
        {
            hasUsages = this._usages.Any();
        }

        var presentation = ErrorStatePresentationCatalog.Create(hasUsages);
        if (!presentation.ReplaceProviderCards)
        {
            // Preserve visible data and only surface status when we have a stale snapshot.
            this.ShowStatus(message, presentation.StatusType);
            return;
        }

        this.ProvidersList.Children.Clear();
        this.ProvidersList.Children.Add(this.CreateInfoTextBlock(message));
        this.ShowStatus(message, presentation.StatusType);
    }

    private TextBlock CreateInfoTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = this.GetResourceBrush("TertiaryText", Brushes.Gray),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10),
        };
    }

    // Event Handlers
#pragma warning disable VSTHRD100 // WPF event handlers require async void signatures.
    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.RefreshDataAsync();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "RefreshBtn_Click failed");
            this.ShowStatus("Refresh failed", StatusType.Error);
        }
    }

    private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.OpenSettingsDialogAsync();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "SettingsBtn_Click failed");
            this.ShowStatus("Settings failed", StatusType.Error);
        }
    }

    internal async Task OpenSettingsDialogAsync()
    {
        this._isSettingsDialogOpen = true;
        bool? settingsResult;

        try
        {
            var owner = this.IsVisible ? this : null;
            settingsResult = await this._dialogService.ShowSettingsAsync(owner);
        }
        finally
        {
            this._isSettingsDialogOpen = false;
            this.EnsureAlwaysOnTop();
        }

        if (settingsResult == true)
        {
            // Reload preferences and refresh data
            await this.InitializeAsync();
            await this.ReloadPreferencesAfterSettingsAsync();
        }
    }

    private async Task ReloadPreferencesAfterSettingsAsync()
    {
        this._preferences = await this._preferencesStore.LoadAsync();
        App.Preferences = this._preferences;
        this._isPrivacyMode = this._preferences.IsPrivacyMode;
        App.SetPrivacyMode(this._isPrivacyMode);
        this._preferencesLoaded = true;

        bool hasUsages;
        lock (this._dataLock)
        {
            hasUsages = this._usages.Count > 0;
        }

        void ApplyUiState()
        {
            this.ApplyPreferences();
            this.UpdatePrivacyButtonState();

            if (hasUsages)
            {
                this.RenderProviders();
                _ = this.UpdateTrayIconsAsync();
            }
        }

        if (this.Dispatcher.CheckAccess())
        {
            ApplyUiState();
        }
        else
        {
            _ = this.Dispatcher.BeginInvoke(ApplyUiState);
        }
    }

    private async void WebBtn_Click(object sender, RoutedEventArgs e)
    {
        await this.OpenWebUIAsync();
    }

    private async Task OpenWebUIAsync()
    {
        try
        {
            await this._browserService.OpenWebUIAsync();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to open Web UI");
            MessageBox.Show(
                $"Failed to open Web UI: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void PrivacyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var newPrivacyMode = !this._isPrivacyMode;
            this._preferences.IsPrivacyMode = newPrivacyMode;
            App.SetPrivacyMode(newPrivacyMode);
            await this.SaveUiPreferencesAsync();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "PrivacyBtn_Click failed");
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }

    private async void AlwaysOnTop_Checked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!this.IsLoaded)
            {
                return;
            }

            this._preferences.AlwaysOnTop = this.AlwaysOnTopCheck.IsChecked ?? true;
            if (this._preferences.AlwaysOnTop)
            {
                this.EnsureAlwaysOnTop();
            }
            else
            {
                this.ApplyTopmostState(false);
            }

            await this.SaveUiPreferencesAsync();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "AlwaysOnTop_Checked failed");
        }
    }

    private async Task Compact_CheckedAsync(object sender, RoutedEventArgs e)
    {
        // No-op (Field removed from UI)
        await Task.CompletedTask;
    }

    private async void ShowUsedToggle_Checked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!this.IsLoaded)
            {
                return;
            }

            this._displayPreferences.SetShowUsedPercentages(this._preferences, this.ShowUsedToggle.IsChecked ?? false);
            await this.SaveUiPreferencesAsync();

            // Refresh the display to show used% vs remaining%
            this.RenderProviders();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "ShowUsedToggle_Checked failed");
        }
    }

    private void RefreshData_NoArgs(object sender, RoutedEventArgs e)
    {
        _ = this.RefreshDataAsync();
    }

    private void ViewChangelogBtn_Click(object sender, RoutedEventArgs e)
    {
        if (this._latestUpdate == null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReleaseUrlCatalog.GetReleasesPageUrl(),
                UseShellExecute = true,
            });
            return;
        }

        this.ShowChangelogWindow(this._latestUpdate);
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
            Background = this.GetResourceBrush("CardBackground", Brushes.Black),
            Foreground = this.GetResourceBrush("PrimaryText", Brushes.White),
        };

        var viewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            Document = this._buildChangelogDocument(updateInfo.ReleaseNotes),
        };

        changelogWindow.Content = viewer;
        changelogWindow.ShowDialog();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (this._isUpdateCheckInProgress)
        {
            this._logger.LogDebug("Skipping overlapping update check request.");
            return;
        }

        try
        {
            this._isUpdateCheckInProgress = true;
            this._latestUpdate = await this._updateChecker.CheckForUpdatesAsync();

            var updateNotificationPresentation = UpdateNotificationPresentationCatalog.Create(
                latestVersion: this._latestUpdate?.Version,
                hasBanner: this.UpdateNotificationBanner != null,
                hasText: this.UpdateText != null);

            if (updateNotificationPresentation.ApplyBannerText && this.UpdateText != null)
            {
                this.UpdateText.Text = updateNotificationPresentation.BannerText;
            }

            if (updateNotificationPresentation.ApplyBannerVisibility && this.UpdateNotificationBanner != null)
            {
                this.UpdateNotificationBanner.Visibility = updateNotificationPresentation.ShowBanner
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Update check failed");
        }
        finally
        {
            this._isUpdateCheckInProgress = false;
        }
    }

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (this._latestUpdate == null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReleaseUrlCatalog.GetLatestReleasePageUrl(),
                UseShellExecute = true,
            });
            return;
        }

        var result = MessageBox.Show(
            $"Download and install version {this._latestUpdate.Version}?\n\nThe application will restart after installation.",
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
                Maximum = 100,
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
                        new TextBlock { Text = $"Downloading version {this._latestUpdate.Version}...", Margin = new Thickness(0, 0, 0, 10) },
                        progressBar,
                    },
                },
            };

            var progress = new Progress<double>(p => progressBar.Value = p);
            progressWindow.Show();

            var success = await this._updateChecker.DownloadAndInstallUpdateAsync(this._latestUpdate, progress);
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
            MessageBox.Show(
                $"Update error: {ex.Message}",
                "Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RestartMonitorAsync()
    {
        try
        {
            var restartingPresentation = MonitorControlPresentationCatalog.CreateRestarting();
            this.ShowStatus(restartingPresentation.Message, restartingPresentation.StatusType);

            // Try to start agent
            var monitorReady = await this._monitorLifecycleService.EnsureAgentRunningAsync();
            var restartResultPresentation = MonitorControlPresentationCatalog.CreateRestartResult(monitorReady);
            this.ShowStatus(restartResultPresentation.Message, restartResultPresentation.StatusType);
            if (restartResultPresentation.TriggerRefreshData)
            {
                await this.RefreshDataAsync();
            }
        }
        catch (Exception ex)
        {
            var restartErrorPresentation = MonitorControlPresentationCatalog.CreateRestartError(ex.Message);
            this.ShowStatus(restartErrorPresentation.Message, restartErrorPresentation.StatusType);
        }
    }

    private async void MonitorToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, _) = await this._monitorLifecycleService.IsAgentRunningWithPortAsync();

            if (isRunning)
            {
                // Stop the agent
                var stoppingPresentation = MonitorControlPresentationCatalog.CreateStopping();
                this.ShowStatus(stoppingPresentation.Message, stoppingPresentation.StatusType);
                var stopped = await this._monitorLifecycleService.StopAgentAsync();
                var stopResultPresentation = MonitorControlPresentationCatalog.CreateStopResult(stopped);
                this.ShowStatus(stopResultPresentation.Message, stopResultPresentation.StatusType);
                if (stopResultPresentation.UpdateToggleButton)
                {
                    this.UpdateMonitorToggleButton(stopResultPresentation.ToggleRunningState);
                }
            }
            else
            {
                // Start the monitor
                var startingPresentation = MonitorControlPresentationCatalog.CreateStarting();
                this.ShowStatus(startingPresentation.Message, startingPresentation.StatusType);
                var monitorReady = await this._monitorLifecycleService.EnsureAgentRunningAsync();
                var startResultPresentation = MonitorControlPresentationCatalog.CreateStartResult(monitorReady);
                this.ShowStatus(startResultPresentation.Message, startResultPresentation.StatusType);
                if (startResultPresentation.UpdateToggleButton)
                {
                    this.UpdateMonitorToggleButton(startResultPresentation.ToggleRunningState);
                }

                if (startResultPresentation.TriggerRefreshData)
                {
                    await this.RefreshDataAsync();
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "MonitorToggleBtn_Click failed");
            this.ShowStatus("Monitor toggle failed", StatusType.Error);
        }
    }
#pragma warning restore VSTHRD100

    private void UpdateMonitorToggleButton(bool isRunning)
    {
        if (this.MonitorToggleBtn != null && this.MonitorToggleIcon != null)
        {
            // Update icon: Play (E768) when stopped, Stop (E71A) when running
            this.MonitorToggleIcon.Text = isRunning ? "\uE71A" : "\uE768";
            this.MonitorToggleBtn.ToolTip = isRunning ? "Stop Monitor" : "Start Monitor";
        }
    }

    private async Task UpdateMonitorToggleButtonStateAsync()
    {
        var (isRunning, _) = await this._monitorLifecycleService.IsAgentRunningWithPortAsync();
        this.Dispatcher.Invoke(() => this.UpdateMonitorToggleButton(isRunning));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.R:
                    this.RefreshBtn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.P:
                    this.PrivacyBtn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Q:
                    this.CloseBtn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.Escape)
        {
            this.CloseBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            this.SettingsBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
