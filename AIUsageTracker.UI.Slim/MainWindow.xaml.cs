// <copyright file="MainWindow.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
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

    private static readonly TimeSpan StartupPollingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NormalPollingInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TrayConfigRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly MainViewModel _viewModel;
    private readonly IMonitorService _monitorService;
    private readonly MonitorLifecycleService _monitorLifecycleService;
    private readonly MonitorStartupOrchestrator _monitorStartupOrchestrator;
    private readonly ILogger<MainWindow> _logger;
    private readonly Func<UpdateChannel, GitHubUpdateChecker> _createUpdateChecker;
    private readonly IDialogService _dialogService;
    private readonly IBrowserService _browserService;
    private readonly Func<string, FrameworkElement> _createProviderIcon;
    private readonly Func<string, FlowDocument> _buildChangelogDocument;
    private readonly UiPreferencesStore _preferencesStore;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly DispatcherTimer _alwaysOnTopTimer;

    private GitHubUpdateChecker _updateChecker;
    private AppPreferences _preferences = new();
    private readonly object _dataLock = new();
    private List<ProviderUsage> _usages = new();
    private List<ProviderConfig> _configs = new();
    private bool _isPrivacyMode = App.IsPrivacyMode;
    private readonly EventHandler<PrivacyChangedEventArgs> _privacyChangedHandler;
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
    private bool _isChangelogOpen;
    private bool _isTooltipOpen;
    private readonly CancellationTokenSource _watchdogCts = new();

    public MainWindow(
        MainViewModel viewModel,
        IMonitorService monitorService,
        MonitorLifecycleService monitorLifecycleService,
        MonitorStartupOrchestrator monitorStartupOrchestrator,
        ILogger<MainWindow> logger,
        Func<UpdateChannel, GitHubUpdateChecker> createUpdateChecker,
        GitHubUpdateChecker updateChecker,
        IDialogService dialogService,
        IBrowserService browserService,
        UiPreferencesStore preferencesStore)
        : this(
            skipUiInitialization: false,
            viewModel,
            monitorService,
            monitorLifecycleService,
            monitorStartupOrchestrator,
            logger,
            createUpdateChecker,
            updateChecker,
            dialogService,
            browserService,
            preferencesStore)
    {
    }

    internal MainWindow(
        bool skipUiInitialization,
        MainViewModel viewModel,
        IMonitorService monitorService,
        MonitorLifecycleService monitorLifecycleService,
        MonitorStartupOrchestrator monitorStartupOrchestrator,
        ILogger<MainWindow> logger,
        Func<UpdateChannel, GitHubUpdateChecker> createUpdateChecker,
        GitHubUpdateChecker updateChecker,
        IDialogService dialogService,
        IBrowserService browserService,
        UiPreferencesStore preferencesStore)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(monitorLifecycleService);
        ArgumentNullException.ThrowIfNull(monitorStartupOrchestrator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(createUpdateChecker);
        ArgumentNullException.ThrowIfNull(updateChecker);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(browserService);
        ArgumentNullException.ThrowIfNull(preferencesStore);

        if (!skipUiInitialization)
        {
            this.InitializeComponent();
            this.ApplyVersionDisplay();
        }

        this._logger = logger;
        this._monitorService = monitorService;
        this._monitorLifecycleService = monitorLifecycleService;
        this._monitorStartupOrchestrator = monitorStartupOrchestrator;
        this._createUpdateChecker = createUpdateChecker;
        this._updateChecker = updateChecker;
        this._dialogService = dialogService;
        this._browserService = browserService;
        var providerIconService = new WpfProviderIconService(this._logger, this.GetResourceBrush);
        this._createProviderIcon = providerIconService.CreateIcon;
        var markdownRenderer = new ChangelogMarkdownRenderer(this.GetResourceBrush);
        this._buildChangelogDocument = markdownRenderer.BuildDocument;
        this._preferencesStore = preferencesStore;
        this._viewModel = viewModel;
        this.DataContext = this._viewModel;
        this._privacyChangedHandler = this.OnPrivacyChanged;

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
                await this.CheckForUpdatesAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this._logger.LogError(ex, "UpdateCheckTimer_Tick failed");
            }
        };
        this._updateCheckTimer.Start();
        this._alwaysOnTopTimer.Tick += (s, e) => this.EnsureAlwaysOnTop();
        this._alwaysOnTopTimer.Start();

        this.SourceInitialized += this.OnSourceInitialized;
        if (OperatingSystem.IsWindows())
        {
            SystemEvents.PowerModeChanged += this.OnPowerModeChanged;
        }

        PrivacyChangedWeakEventManager.AddHandler(this._privacyChangedHandler);
        this.Closed += (s, e) =>
        {
            PrivacyChangedWeakEventManager.RemoveHandler(this._privacyChangedHandler);
            this._watchdogCts.Cancel();
            this._watchdogCts.Dispose();
            if (OperatingSystem.IsWindows())
            {
                SystemEvents.PowerModeChanged -= this.OnPowerModeChanged;
            }

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
                await this.InitializeAsync().ConfigureAwait(true);
                _ = this.CheckForUpdatesAsync().ContinueWith(
                    t => this._logger.LogError(t.Exception, "CheckForUpdatesAsync failed unhandled"),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this._logger.LogError(ex, "Window_Loaded failed");
            }
        };

        // Track window position changes
        this.LocationChanged += async (s, e) =>
        {
            try
            {
                await this.SaveWindowPositionAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this._logger.LogError(ex, "LocationChanged handler failed");
            }
        };
        this.SizeChanged += async (s, e) =>
        {
            try
            {
                await this.SaveWindowPositionAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
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

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var prereleaseLabel = ParsePrereleaseLabel(informationalVersion);
        var displayVersion = string.IsNullOrWhiteSpace(prereleaseLabel)
            ? $"v{versionCore}"
            : $"v{versionCore} {prereleaseLabel}";
        this.VersionText.Text = displayVersion;
        this.Title = $"AI Usage Tracker {displayVersion}";
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
            await this.SaveUiPreferencesAsync().ConfigureAwait(true);
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
                // Use preferences already loaded by App.OnStartup — don't reload from disk
                this._preferences = App.Preferences;
                this._isPrivacyMode = this._preferences.IsPrivacyMode;
                this._preferencesLoaded = true;
                this.ApplyPreferences();
                this.PositionWindowNearTray();
            }

            // Monitor warmup was fired in App.OnStartup in parallel with WPF init.
            // By now it should already be done or nearly done. Just await it.
            this.LogDiagnostic("[DIAGNOSTIC] Awaiting monitor warmup task...");
            this.ShowStatus("Loading...", StatusType.Info);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var monitorReady = await App.MonitorWarmupTask.ConfigureAwait(true);
                sw.Stop();
                this.LogDiagnostic($"[DIAGNOSTIC] Monitor warmup completed in {sw.ElapsedMilliseconds}ms, ready={monitorReady}");

                if (!monitorReady)
                {
                    // Warmup failed — try the orchestrator as fallback
                    this.ShowStatus("Starting monitor...", StatusType.Info);
                    var startupResult = await this._monitorStartupOrchestrator.EnsureMonitorReadyAsync(
                        async (message, type) =>
                        {
                            await this.Dispatcher.InvokeAsync(() => this.ShowStatus(message, type)).Task.ConfigureAwait(true);
                        },
                        skipInitialHealthCheck: true).ConfigureAwait(true);

                    if (!startupResult.IsSuccess)
                    {
                        if (startupResult.IsLaunchFailure)
                        {
                            this.ShowStatus("Monitor failed to start", StatusType.Error);
                            this.ShowErrorState(
                                BuildMonitorErrorMessage(
                                    "Monitor failed to start.",
                                    "Please ensure AIUsageTracker.Monitor is installed and try again.",
                                    this._monitorService.LastAgentErrors));
                        }

                        this._isLoading = false;
                        return;
                    }
                }

                // Monitor is running — refresh port and fetch data
                await this._monitorService.RefreshPortAsync().ConfigureAwait(true);
                await this.FetchDataAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this.LogDiagnostic($"[DIAGNOSTIC] Monitor startup failed: {ex.Message}");
                this.ShowErrorState($"Monitor startup failed: {ex.Message}");
                this._isLoading = false;
                return;
            }

            // Start background tasks (non-blocking)
            this.StartPollingTimer();
            _ = this.InitializeSignalRAsync();
            _ = Task.Run(async () =>
            {
                // Contract check and toggle update in background — don't block UI
                var handshakeResult = await this._monitorService.CheckApiContractAsync().ConfigureAwait(false); // ui-thread-guardrail-allow: Task.Run thread pool
                await this.Dispatcher.InvokeAsync(() => this.ApplyMonitorContractStatus(handshakeResult)).Task.ConfigureAwait(true);
            });
            _ = Task.Run(() => this._monitorStartupOrchestrator.RunWatchdogLoopAsync(this._watchdogCts.Token));

            this.ShowStatus("Connected", StatusType.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.ShowErrorState($"Initialization failed: {ex.Message}");
        }
        finally
        {
            this._isLoading = false;
        }
    }

    private void ApplyPreferences()
    {
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

    /// <summary>
    /// Re-syncs UI controls from <see cref="_preferences"/> and re-renders all cards.
    /// Used by the card catalog screenshot generator after applying each permutation.
    /// </summary>
    internal void ApplyPreferencesAndRerender()
    {
        this.ApplyDisplayModePreference();
        this.RenderProviders();
    }

    private void ApplyDisplayModePreference()
    {
        if (this.ShowUsedToggle != null)
        {
            this.ShowUsedToggle.IsChecked = this._preferences.PercentageDisplayMode == PercentageDisplayMode.Used;
        }
    }

    private void InitializeUpdateChecker()
    {
        if (this._preferences == null)
        {
            return;
        }

        var channel = this._preferences.UpdateChannel;
        this._updateChecker = this._createUpdateChecker(channel);
    }

    private async Task SaveUiPreferencesAsync()
    {
        App.Preferences = this._preferences;
        var saved = await this._preferencesStore.SaveAsync(this._preferences).ConfigureAwait(true);
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
            await this.InitializeAsync().ConfigureAwait(true);
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

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            this._logger.LogInformation("System resumed — triggering immediate watchdog check");
            this._monitorStartupOrchestrator.NotifyResumed();
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
            : this.GetResourceBrush("SecondaryText", Brushes.Gray);
    }

    private SolidColorBrush GetResourceBrush(string key, SolidColorBrush fallback) =>
        UIHelper.GetResourceBrush(key, fallback);

    private void LogDiagnostic(string message)
    {
        this._logger.LogInformation("{DiagnosticMessage}", message);
        UiDiagnosticFileLog.Write(message);
    }

    // Event Handlers
#pragma warning disable VSTHRD100 // WPF event handlers require async void signatures.
    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.RefreshDataAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(ex, "RefreshBtn_Click failed");
            this.ShowStatus("Refresh failed", StatusType.Error);
        }
    }

    private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this.OpenSettingsDialogAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
            settingsResult = await this._dialogService.ShowSettingsAsync(owner).ConfigureAwait(true);
        }
        finally
        {
            this._isSettingsDialogOpen = false;
            this.EnsureAlwaysOnTop();
        }

        if (settingsResult == true)
        {
            // Read updated preferences from the shared in-memory object.
            this._preferences = App.Preferences;
            this._isPrivacyMode = this._preferences.IsPrivacyMode;
            App.SetPrivacyMode(this._isPrivacyMode);
            this._preferencesLoaded = true;

            this.ApplyPreferencesFromSettings();
            await this.InitializeAsync().ConfigureAwait(true);
        }
    }

    private void ApplyPreferencesFromSettings()
    {
        if (this._isLoading)
        {
            return;
        }

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
        await this.OpenWebUIAsync().ConfigureAwait(true);
    }

    private async Task OpenWebUIAsync()
    {
        try
        {
            await this._browserService.OpenWebUIAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
            await this.SaveUiPreferencesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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

            await this.SaveUiPreferencesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(ex, "AlwaysOnTop_Checked failed");
        }
    }

    private async void ShowUsedToggle_Checked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!this.IsLoaded)
            {
                return;
            }

            this._preferences.ShowUsedPercentages = this.ShowUsedToggle.IsChecked ?? false;
            await this.SaveUiPreferencesAsync().ConfigureAwait(true);

            // Refresh the display to show used% vs remaining%
            this.RenderProviders();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(ex, "ShowUsedToggle_Checked failed");
        }
    }

    private static string BuildMonitorErrorMessage(
        string heading,
        string fallbackDetails,
        IReadOnlyCollection<string>? errors)
    {
        if (errors == null || errors.Count == 0)
        {
            return $"{heading}\n\n{fallbackDetails}";
        }

        var details = string.Join(
            Environment.NewLine,
            errors.Take(3).Select(error => $"- {error}"));

        return $"{heading}\n\nMonitor reported:\n{details}\n\n{fallbackDetails}";
    }

    private static string? ParsePrereleaseLabel(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var normalized = informationalVersion.Split('+')[0];
        var dashIndex = normalized.IndexOf("-", StringComparison.Ordinal);
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

    private async void MonitorToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, _) = await this._monitorLifecycleService.IsAgentRunningWithPortAsync().ConfigureAwait(true);

            if (isRunning)
            {
                // Stop the agent
                this.ShowStatus("Stopping monitor...", StatusType.Warning);
                var stopped = await this._monitorLifecycleService.StopAgentAsync().ConfigureAwait(true);
                if (stopped)
                {
                    this.ShowStatus("Monitor stopped", StatusType.Info);
                    this.UpdateMonitorToggleButton(false);
                }
                else
                {
                    this.ShowStatus("Failed to stop monitor", StatusType.Error);
                }
            }
            else
            {
                // Start the monitor
                this.ShowStatus("Starting monitor...", StatusType.Warning);
                var monitorReady = await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(true);
                if (monitorReady)
                {
                    this.ShowStatus("Monitor started", StatusType.Success);
                    this.UpdateMonitorToggleButton(true);
                    await this.RefreshDataAsync().ConfigureAwait(true);
                }
                else
                {
                    this.ShowStatus("Monitor failed to start", StatusType.Error);
                    this.UpdateMonitorToggleButton(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
            this.MonitorToggleIcon.Text = isRunning ? "\uE71A" : "\uE768";
            this.MonitorToggleBtn.ToolTip = isRunning ? "Stop Monitor" : "Start Monitor";
        }
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
                    return;
                case Key.P:
                    this.PrivacyBtn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.Q:
                    this.CloseBtn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Escape:
                this.CloseBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            case Key.F2:
                this.SettingsBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            default:
                return;
        }
    }
}
