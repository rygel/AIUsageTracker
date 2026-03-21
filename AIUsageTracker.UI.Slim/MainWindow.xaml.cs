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
                    BuildMonitorErrorMessage(
                        "Monitor failed to start.",
                        "Please ensure AIUsageTracker.Monitor is installed and try again.",
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

                if (!hasUsages && this.ProvidersList.Children.Count <= 1)
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
                BuildMonitorErrorMessage(
                    "Cannot connect to Monitor.",
                    "Please ensure:\n1. Monitor is running\n2. Port is correct (check monitor.json)\n3. Firewall is not blocking\n\nTry restarting the Monitor.",
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
            : this.GetResourceBrush("SecondaryText", Brushes.Gray);
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

            if (hasLatestUsages)
            {
                lock (this._dataLock)
                {
                    this._usages = latestUsages.ToList();
                }

                this.RenderProviders();
                this._lastMonitorUpdate = now;
                this.ShowStatus($"{now:HH:mm:ss}", StatusType.Success);
                _ = this.UpdateTrayIconsAsync();
            }
            else if (hasCurrentUsages)
            {
                this.ShowStatus("Refresh returned no data, keeping last snapshot", StatusType.Warning);
            }
            else
            {
                this.ShowErrorState("No provider data available.\n\nMonitor may still be initializing.");
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
        var renderPlan = MainWindowRuntimeLogic.BuildProviderRenderPlan(usagesCopy, this._preferences.HiddenProviderItemIds);
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
                        () => MainWindowRuntimeLogic.GetSectionIsCollapsed(this._preferences, section.IsQuotaBased),
                        v => MainWindowRuntimeLogic.SetSectionIsCollapsed(this._preferences, section.IsQuotaBased, v));

                this.ProvidersList.Children.Add(header);
                this.ProvidersList.Children.Add(container);

            var isCollapsed = MainWindowRuntimeLogic.GetSectionIsCollapsed(this._preferences, section.IsQuotaBased);
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
        var margin = isGroupHeader
            ? new Thickness(0, 8, 0, 4)
            : new Thickness(20, 4, 0, 2);
        var toggleFontSize = isGroupHeader ? 10.0 : 9.0;
        var titleFontWeight = isGroupHeader ? FontWeights.Bold : FontWeights.Normal;
        var toggleOpacity = isGroupHeader ? 1.0 : 0.8;
        var lineOpacity = isGroupHeader ? 0.5 : 0.3;
        var titleText = isGroupHeader ? title.ToUpperInvariant() : title;
        var titleForeground = isGroupHeader
            ? accent
            : this.GetResourceBrush("SecondaryText", Brushes.Gray);

        var header = this.CreateCollapsibleHeaderGrid(margin);

        // Toggle button
        var toggleText = this.CreateText(
            getCollapsed() ? "▶" : "▼",
            toggleFontSize,
            accent,
            FontWeights.Bold,
            new Thickness(0, 0, 5, 0));
        toggleText.VerticalAlignment = VerticalAlignment.Center;
        toggleText.Opacity = toggleOpacity;
        toggleText.Tag = "ToggleIcon";

        // Title
        var titleBlock = this.CreateText(
            titleText,
            10.0,
            titleForeground,
            titleFontWeight,
            new Thickness(0, 0, 10, 0));
        titleBlock.VerticalAlignment = VerticalAlignment.Center;

        // Separator line
        var line = this.CreateSeparator(accent, lineOpacity);

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
            nextReset => FormatRelativeTimeUntil(nextReset, DateTime.Now));
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
        var sectionOpt = MainWindowRuntimeLogic.Build(usage, this._preferences);
        if (sectionOpt is null)
        {
            return;
        }
        var section = sectionOpt.Value;

        var (subHeader, subContainer) = this.CreateCollapsibleHeader(
            section.Title,
            Brushes.DeepSkyBlue,
            isGroupHeader: false,
            groupKey: null,
            () => MainWindowRuntimeLogic.GetIsCollapsed(this._preferences, section.ProviderId),
            v => MainWindowRuntimeLogic.SetIsCollapsed(this._preferences, section.ProviderId, v));

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
            Interval = hasUsages ? NormalPollingInterval : StartupPollingInterval,
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
                    var refreshDecision = MainWindowRuntimeLogic.CreatePollingRefreshDecision(
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

                    var now = DateTime.Now;
                    string? noDataMessage = null;
                    StatusType? noDataStatusType = null;
                    var switchToStartupInterval = false;
                    if (!hasCurrentUsages)
                    {
                        noDataMessage = "No data - waiting for Monitor";
                        noDataStatusType = StatusType.Warning;
                        switchToStartupInterval = true;
                    }
                    else if ((now - this._lastMonitorUpdate).TotalMinutes > 5)
                    {
                        noDataMessage = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(this._lastMonitorUpdate, now);
                        noDataStatusType = StatusType.Warning;
                    }

                    if (noDataMessage != null && noDataStatusType.HasValue)
                    {
                        this.ShowStatus(noDataMessage, noDataStatusType.Value);
                    }

                    if (switchToStartupInterval &&
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

                var now = DateTime.Now;
                string? exceptionMessage;
                StatusType exceptionStatusType;
                var switchToStartupInterval = false;
                if (hasOldData)
                {
                    exceptionMessage = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(this._lastMonitorUpdate, now);
                    exceptionStatusType = StatusType.Warning;
                }
                else
                {
                    exceptionMessage = "Connection error";
                    exceptionStatusType = StatusType.Error;
                    switchToStartupInterval = true;
                }
                this.ShowStatus(exceptionMessage, exceptionStatusType);

                if (switchToStartupInterval &&
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

            var shouldRefreshConfigs = MainWindowRuntimeLogic.ShouldRefreshTrayConfigs(
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
                var successStatusMessage = $"{now:HH:mm:ss}{statusSuffix}";
                var switchToNormalInterval = this._pollingTimer != null
                    && this._pollingTimer.Interval != NormalPollingInterval;

                lock (this._dataLock)
                {
                    this._usages = usages.ToList();
                }

                this.RenderProviders();
                this._lastMonitorUpdate = now;
                this.ShowStatus(successStatusMessage, StatusType.Success);
                _ = this.UpdateTrayIconsAsync();

                if (switchToNormalInterval && this._pollingTimer != null)
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
        return MainWindowRuntimeLogic.FormatMonitorOfflineStatus(this._lastMonitorUpdate, DateTime.Now);
    }

    private void LogDiagnostic(string message)
    {
        this._logger.LogInformation("{DiagnosticMessage}", message);
        UiDiagnosticFileLog.Write(message);
    }

    private void ShowStatus(string message, StatusType type)
    {
        var effectiveMessage = message;
        var effectiveType = type;
        if (effectiveType == StatusType.Success &&
            !string.IsNullOrWhiteSpace(this._monitorContractWarningMessage))
        {
            effectiveMessage = this._monitorContractWarningMessage;
            effectiveType = StatusType.Warning;
        }

        var tooltipText = this._lastMonitorUpdate == DateTime.MinValue
            ? "Last update: Never"
            : $"Last update: {this._lastMonitorUpdate:HH:mm:ss}";

        var indicatorKind = effectiveType switch
        {
            StatusType.Success => StatusIndicatorKind.Success,
            StatusType.Warning => StatusIndicatorKind.Warning,
            StatusType.Error => StatusIndicatorKind.Error,
            _ => StatusIndicatorKind.Neutral,
        };

        var logLevel = effectiveType switch
        {
            StatusType.Error => LogLevel.Error,
            StatusType.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        if (this.StatusText != null)
        {
            this.StatusText.Text = effectiveMessage;
        }

        // Update LED color
        if (this.StatusLed != null)
        {
            this.StatusLed.Fill = indicatorKind switch
            {
                StatusIndicatorKind.Success => this.GetResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen),
                StatusIndicatorKind.Warning => Brushes.Gold,
                StatusIndicatorKind.Error => this.GetResourceBrush("ProgressBarRed", Brushes.Crimson),
                _ => this.GetResourceBrush("SecondaryText", Brushes.Gray),
            };
        }

        if (this.StatusLed != null)
        {
            this.StatusLed.ToolTip = this.CreateTopmostAwareToolTip(this.StatusLed, tooltipText);
        }

        if (this.StatusText != null)
        {
            this.StatusText.ToolTip = this.CreateTopmostAwareToolTip(this.StatusText, tooltipText);
        }

        this._logger.Log(
            logLevel,
            "[{StatusType}] {StatusMessage}",
            effectiveType,
            effectiveMessage);
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

        if (hasUsages)
        {
            // Preserve visible data and only surface status when we have a stale snapshot.
            this.ShowStatus(message, StatusType.Warning);
            return;
        }

        this.ProvidersList.Children.Clear();
        this.ProvidersList.Children.Add(this.CreateInfoTextBlock(message));
        this.ShowStatus(message, StatusType.Error);
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

    private async void ShowUsedToggle_Checked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!this.IsLoaded)
            {
                return;
            }

            this._preferences.ShowUsedPercentages = this.ShowUsedToggle.IsChecked ?? false;
            await this.SaveUiPreferencesAsync();

            // Refresh the display to show used% vs remaining%
            this.RenderProviders();
        }
        catch (Exception ex)
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

    private static string FormatRelativeTimeUntil(DateTime nextReset, DateTime now)
    {
        var diff = nextReset - now;

        if (diff.TotalSeconds <= 0)
        {
            return "0m";
        }

        if (diff.TotalDays >= 1)
        {
            return $"{diff.Days}d {diff.Hours}h";
        }

        if (diff.TotalHours >= 1)
        {
            return $"{diff.Hours}h {diff.Minutes}m";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes))}m";
    }

    private static string? ParsePrereleaseLabel(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

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

    private async void MonitorToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, _) = await this._monitorLifecycleService.IsAgentRunningWithPortAsync();

            if (isRunning)
            {
                // Stop the agent
                this.ShowStatus("Stopping monitor...", StatusType.Warning);
                var stopped = await this._monitorLifecycleService.StopAgentAsync();
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
                var monitorReady = await this._monitorLifecycleService.EnsureAgentRunningAsync();
                if (monitorReady)
                {
                    this.ShowStatus("Monitor started", StatusType.Success);
                    this.UpdateMonitorToggleButton(true);
                    await this.RefreshDataAsync();
                }
                else
                {
                    this.ShowStatus("Monitor failed to start", StatusType.Error);
                    this.UpdateMonitorToggleButton(false);
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


