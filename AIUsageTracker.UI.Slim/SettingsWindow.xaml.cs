// <copyright file="SettingsWindow.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow : Window
{
    private static readonly JsonSerializerOptions BundleJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly IMonitorService _monitorService;
    private readonly IMonitorLifecycleService _monitorLifecycleService;
    private readonly ILogger<SettingsWindow> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly UiPreferencesStore _preferencesStore;
    private readonly SemaphoreSlim _autoSaveSemaphore = new(1, 1);
    private readonly DispatcherTimer _autoSaveTimer;

    private List<ProviderConfig> _configs = new();
    private List<ProviderUsage> _usages = new();
    private string? _gitHubAuthUsername;
    private string? _openAiAuthUsername;
    private string? _codexAuthUsername;
    private string? _antigravityAuthUsername;
    private AppPreferences _preferences = new();
    private bool _isPrivacyMode = App.IsPrivacyMode;
    private bool _isDeterministicScreenshotMode;
    private bool _isLoadingSettings;
    private bool _hasPendingAutoSave;

    public SettingsWindow(
        IMonitorService monitorService,
        IMonitorLifecycleService monitorLifecycleService,
        ILogger<SettingsWindow> logger,
        UiPreferencesStore preferencesStore,
        IAppPathProvider pathProvider)
    {
        this._autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        this._autoSaveTimer.Tick += this.AutoSaveTimer_Tick;

        this.InitializeComponent();
        this._monitorService = monitorService;
        this._monitorLifecycleService = monitorLifecycleService;
        this._logger = logger;
        this._pathProvider = pathProvider;
        this._preferencesStore = preferencesStore;
        App.PrivacyChanged += this.OnPrivacyChanged;
        this.Closed += this.SettingsWindow_Closed;
        this.Loaded += this.SettingsWindow_Loaded;
        this.UpdatePrivacyButtonState();
    }

    public SettingsWindow()
        : this(
        App.Host.Services.GetRequiredService<IMonitorService>(),
        App.Host.Services.GetRequiredService<IMonitorLifecycleService>(),
        App.Host.Services.GetRequiredService<ILogger<SettingsWindow>>(),
        App.Host.Services.GetRequiredService<UiPreferencesStore>(),
        App.Host.Services.GetRequiredService<IAppPathProvider>())
    {
    }

    internal bool SettingsChanged { get; private set; }

#pragma warning disable VSTHRD100 // WPF event handlers require async void signatures.
    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await this._monitorService.RefreshPortAsync();
            await this._monitorService.RefreshAgentInfoAsync();
            await this.LoadDataAsync();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Settings load failed");
            MessageBox.Show(
                $"Failed to load Settings: {ex.Message}",
                "Settings Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            this.Close();
        }
    }

    private async Task LoadDataAsync()
    {
        this._isLoadingSettings = true;
        string? loadError = null;

        try
        {
            this._isDeterministicScreenshotMode = false;

            this._configs = (await this._monitorService.GetConfigsAsync().ConfigureAwait(true)).ToList();
            this._usages = (await this._monitorService.GetUsageAsync().ConfigureAwait(true)).ToList();

            if (this._configs.Count == 0)
            {
                loadError = "No providers found. This may indicate:\n" +
                           "- Monitor is not running\n" +
                           "- Failed to connect to Monitor\n" +
                           "- No providers configured in Monitor\n\n" +
                           "Try clicking 'Refresh Data' or restarting the Monitor.";
            }

            this._gitHubAuthUsername = await ProviderAuthIdentityDiscovery.TryGetGitHubUsernameAsync(this._logger).ConfigureAwait(true);
            this._openAiAuthUsername = await ProviderAuthIdentityDiscovery.TryGetOpenAiUsernameAsync(this._logger).ConfigureAwait(true);
            this._codexAuthUsername = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(this._logger).ConfigureAwait(true);
            this._antigravityAuthUsername = await ProviderAuthIdentityDiscovery.TryGetAntigravityUsernameAsync(this._logger).ConfigureAwait(true);
            this._preferences = await this._preferencesStore.LoadAsync().ConfigureAwait(true);
            App.Preferences = this._preferences;
            this._isPrivacyMode = this._preferences.IsPrivacyMode;
            App.SetPrivacyMode(this._isPrivacyMode);
            this.UpdatePrivacyButtonState();

            this.PopulateProviders();
            this.RefreshTrayIcons();
            this.PopulateLayoutSettings();
            await this.LoadHistoryAsync().ConfigureAwait(true);
            await this.UpdateMonitorStatusAsync().ConfigureAwait(true);
            this.RefreshDiagnosticsLog();
        }
        catch (HttpRequestException ex)
        {
            loadError = $"Failed to connect to Monitor: {ex.Message}\n\n" +
                       "Ensure the Monitor is running and accessible.";
        }
        catch (Exception ex)
        {
            loadError = $"Failed to load settings: {ex.Message}";
        }
        finally
        {
            this._isLoadingSettings = false;

            if (loadError != null)
            {
                MessageBox.Show(
                    loadError,
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

#pragma warning disable VSTHRD001 // Headless screenshot capture intentionally waits for dispatcher idle before rendering.
    internal async Task PrepareForHeadlessScreenshotAsync(bool deterministic = false)
    {
        if (deterministic)
        {
            this.PrepareDeterministicScreenshotData();
        }
        else
        {
            await this.LoadDataAsync();
        }

        await this.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        this.UpdateLayout();
    }

    internal async Task<IReadOnlyList<string>> CaptureHeadlessTabScreenshotsAsync(string outputDirectory)
    {
        await this.PrepareForHeadlessScreenshotAsync(deterministic: true);

        var capturedFiles = new List<string>();
        if (this.MainTabControl.Items.Count == 0)
        {
            const string fallbackName = "screenshot_settings_privacy.png";
            App.RenderWindowContent(this, Path.Combine(outputDirectory, fallbackName));
            capturedFiles.Add(fallbackName);
            return capturedFiles;
        }

        for (var index = 0; index < this.MainTabControl.Items.Count; index++)
        {
            this.MainTabControl.SelectedIndex = index;
            await this.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            var header = (this.MainTabControl.Items[index] as TabItem)?.Header?.ToString();
            this.ApplyHeadlessCaptureWindowSize(header);
            await this.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            this.UpdateLayout();

            var tabSlug = this.BuildTabSlug(header, index);
            var fileName = $"screenshot_settings_{tabSlug}_privacy.png";
            App.RenderWindowContent(this, Path.Combine(outputDirectory, fileName));
            capturedFiles.Add(fileName);
        }

        this.MainTabControl.SelectedIndex = 0;
        await this.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        this.UpdateLayout();

        return capturedFiles;
    }
#pragma warning restore VSTHRD001

    private void ApplyHeadlessCaptureWindowSize(string? tabHeader)
    {
        this.Width = 600;
        this.Height = 600;

        if (!this._isDeterministicScreenshotMode)
        {
            return;
        }

        if (!string.Equals(tabHeader, "Providers", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        this.Width = 760;
        this.ProvidersStack.Measure(new Size(this.Width - 80, double.PositiveInfinity));
        var desiredContentHeight = this.ProvidersStack.DesiredSize.Height;
        this.Height = Math.Max(900, Math.Min(3200, desiredContentHeight + 260));
    }

    private void PrepareDeterministicScreenshotData()
    {
        this._isDeterministicScreenshotMode = true;
        this._preferences = new AppPreferences
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
            IsPrivacyMode = true,
        };

        App.Preferences = this._preferences;
        this._isPrivacyMode = true;
        App.SetPrivacyMode(true);
        this.UpdatePrivacyButtonState();

        var fixture = SettingsWindowDeterministicFixture.Create();
        this._configs = fixture.Configs;
        this._usages = fixture.Usages;

        this.PopulateProviders();
        this.PopulateLayoutSettings();

        this.HistoryDataGrid.ItemsSource = fixture.HistoryRows;

        if (this.MonitorStatusText != null)
        {
            this.MonitorStatusText.Text = fixture.MonitorStatusText;
        }

        if (this.MonitorPortText != null)
        {
            this.MonitorPortText.Text = fixture.MonitorPortText;
        }

        if (this.MonitorLogsText != null)
        {
            this.MonitorLogsText.Text = fixture.MonitorLogsText;
        }
    }

    private string BuildTabSlug(string? header, int index)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return $"tab{index + 1}";
        }

        var builder = new StringBuilder();
        foreach (var character in header.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if ((character == ' ' || character == '-' || character == '_') &&
                     builder.Length > 0 &&
                     builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? $"tab{index + 1}" : normalized;
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        this._autoSaveTimer.Stop();
        App.PrivacyChanged -= this.OnPrivacyChanged;
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            this._autoSaveTimer.Stop();
            await this.PersistAllSettingsAsync(showErrorDialog: false);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "AutoSaveTimer_Tick failed");
        }
    }

    private void ScheduleAutoSave()
    {
        if (this._isLoadingSettings)
        {
            return;
        }

        this._hasPendingAutoSave = true;
        this._autoSaveTimer.Stop();
        this._autoSaveTimer.Start();
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
        this.PopulateProviders();
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

    private async Task UpdateMonitorStatusAsync()
    {
        try
        {
            // Check if agent is running
            var isRunning = await this._monitorLifecycleService.IsAgentRunningAsync().ConfigureAwait(true);

            // Get the actual port from the agent
            int port = await this._monitorLifecycleService.GetAgentPortAsync().ConfigureAwait(true);

            if (this.MonitorStatusText != null)
            {
                this.MonitorStatusText.Text = isRunning ? "Running" : "Not Running";
            }

            // Update port display
            if (this.FindName("MonitorPortText") is TextBlock portText)
            {
                portText.Text = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to update monitor status");
            if (this.MonitorStatusText != null)
            {
                this.MonitorStatusText.Text = "Error";
            }
        }
        finally
        {
            this.RefreshDiagnosticsLog();
        }
    }

    private void RefreshDiagnosticsLog()
    {
        if (this.MonitorLogsText == null)
        {
            return;
        }

        if (this._isDeterministicScreenshotMode)
        {
            this.MonitorLogsText.Text = "Monitor health check: OK" + Environment.NewLine +
                                 "Diagnostics available in Settings > Monitor.";
            this.MonitorLogsText.ScrollToEnd();
            return;
        }

        var logs = MonitorService.DiagnosticsLog;
        var lines = new List<string>();
        if (logs.Count == 0)
        {
            lines.Add("No diagnostics captured yet.");
        }
        else
        {
            lines.AddRange(logs);
        }

        var telemetry = MonitorService.GetTelemetrySnapshot();
        lines.Add("---- Slim Telemetry ----");
        lines.Add(
            $"Usage: count={telemetry.UsageRequestCount}, avg={telemetry.UsageAverageLatencyMs:F1}ms, last={telemetry.UsageLastLatencyMs}ms, errors={telemetry.UsageErrorCount} ({telemetry.UsageErrorRatePercent:F1}%)");
        lines.Add(
            $"Refresh: count={telemetry.RefreshRequestCount}, avg={telemetry.RefreshAverageLatencyMs:F1}ms, last={telemetry.RefreshLastLatencyMs}ms, errors={telemetry.RefreshErrorCount} ({telemetry.RefreshErrorRatePercent:F1}%)");

        this.MonitorLogsText.Text = string.Join(Environment.NewLine, lines);
        this.MonitorLogsText.ScrollToEnd();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await this._monitorService.GetHistoryAsync(100);
            this.HistoryDataGrid.ItemsSource = history;
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to load history");
        }
    }

    private void PopulateProviders()
    {
        this.ProvidersStack.Children.Clear();

        var displayItems = ProviderSettingsDisplayCatalog.CreateDisplayItems(this._configs, this._usages);
        var usageByProviderId = this._usages.ToDictionary(usage => usage.ProviderId, StringComparer.OrdinalIgnoreCase);

        foreach (var item in displayItems)
        {
            usageByProviderId.TryGetValue(item.Config.ProviderId, out var usage);
            this.AddProviderCard(item.Config, usage, item.IsDerived);
        }
    }

    private void AddProviderCard(ProviderConfig config, ProviderUsage? usage, bool isDerived = false)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10, 8, 10, 8),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Inputs

        var settingsBehavior = ProviderSettingsCatalog.Resolve(config, usage, isDerived);
        var headerPanel = this.BuildProviderHeader(config, settingsBehavior, isDerived);

        grid.Children.Add(headerPanel);

        // Input row
        var keyPanel = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyContent = this.BuildProviderInputContent(config, usage, settingsBehavior);
        Grid.SetColumn(keyContent, 0);
        keyPanel.Children.Add(keyContent);

        Grid.SetRow(keyPanel, 1);
        grid.Children.Add(keyPanel);

        var subTrayDetails = ProviderSubTrayCatalog.GetEligibleDetails(usage);

        if (!isDerived && subTrayDetails is { Count: > 0 })
        {
            this.AddSubTraySection(grid, config, subTrayDetails);
        }

        card.Child = grid;
        this.ProvidersStack.Children.Add(card);
    }

    private FrameworkElement BuildProviderInputContent(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        return settingsBehavior.InputMode switch
        {
            ProviderInputMode.DerivedReadOnly
                or ProviderInputMode.AntigravityAutoDetected
                or ProviderInputMode.GitHubCopilotAuthStatus
                or ProviderInputMode.OpenAiSessionStatus
                => this.BuildStatusPanel(config, usage, settingsBehavior),
            _ => this.BuildApiKeyEditor(config),
        };
    }

    private StackPanel BuildStatusPanel(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage,
            settingsBehavior.InputMode,
            this._isPrivacyMode,
            new ProviderAuthIdentities(
                this._gitHubAuthUsername,
                this._openAiAuthUsername,
                this._codexAuthUsername,
                this._antigravityAuthUsername));

        var panel = new StackPanel
        {
            Orientation = presentation.UseHorizontalLayout ? Orientation.Horizontal : Orientation.Vertical,
        };

        var statusText = new TextBlock
        {
            Text = presentation.PrimaryText,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontStyle = presentation.PrimaryItalic ? FontStyles.Italic : FontStyles.Normal,
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, presentation.PrimaryResourceKey);
        panel.Children.Add(statusText);

        foreach (var line in presentation.SecondaryLines)
        {
            var secondaryText = this.CreateSecondaryStatusText(line.Text);
            secondaryText.TextWrapping = line.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (line.ExtraTopMargin)
            {
                secondaryText.Margin = new Thickness(0, 4, 0, 0);
            }

            panel.Children.Add(secondaryText);
        }

        return panel;
    }

    private StackPanel BuildProviderHeader(ProviderConfig config, ProviderSettingsBehavior settingsBehavior, bool isDerived)
    {
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

        var icon = this.CreateProviderIcon(config.ProviderId);
        icon.Width = 16;
        icon.Height = 16;
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        headerPanel.Children.Add(icon);

        var title = new TextBlock
        {
            Text = ProviderMetadataCatalog.GetDisplayName(config.ProviderId),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 120,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        headerPanel.Children.Add(title);

        headerPanel.Children.Add(this.CreateProviderHeaderCheckBox(
            content: "Tray",
            isChecked: config.ShowInTray,
            margin: new Thickness(12, 0, 0, 0),
            isEnabled: !isDerived,
            onCheckedChanged: isChecked =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.ShowInTray = isChecked;
                this.MarkSettingsChanged(refreshTrayIcons: true);
            }));

        headerPanel.Children.Add(this.CreateProviderHeaderCheckBox(
            content: "Notify",
            isChecked: config.EnableNotifications,
            margin: new Thickness(8, 0, 0, 0),
            isEnabled: !isDerived,
            onCheckedChanged: isChecked =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.EnableNotifications = isChecked;
                this.MarkSettingsChanged();
            }));

        if (settingsBehavior.IsInactive)
        {
            headerPanel.Children.Add(this.CreateInactiveBadge());
        }

        return headerPanel;
    }

    private CheckBox CreateProviderHeaderCheckBox(
        string content,
        bool isChecked,
        Thickness margin,
        bool isEnabled,
        Action<bool> onCheckedChanged)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            IsChecked = isChecked,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = margin,
            IsEnabled = isEnabled,
        };
        checkBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        checkBox.Checked += (_, _) => onCheckedChanged(true);
        checkBox.Unchecked += (_, _) => onCheckedChanged(false);
        return checkBox;
    }

    private Border CreateInactiveBadge()
    {
        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(205, 92, 92)),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8, 3, 8, 3),
        };

        status.Child = new TextBlock
        {
            Text = "Inactive",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            FontWeight = FontWeights.SemiBold,
        };
        return status;
    }

    private void AddSubTraySection(Grid grid, ProviderConfig config, IReadOnlyList<ProviderUsageDetail> subTrayDetails)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8, 0, 8),
        };
        separator.SetResourceReference(Border.BackgroundProperty, "Separator");
        Grid.SetRow(separator, 2);
        grid.Children.Add(separator);

        var subTrayPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };

        var subTrayTitle = new TextBlock
        {
            Text = "Sub-tray icons",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        subTrayTitle.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        subTrayPanel.Children.Add(subTrayTitle);

        foreach (var detail in subTrayDetails)
        {
            subTrayPanel.Children.Add(this.CreateSubTrayCheckBox(config, detail.Name));
        }

        Grid.SetRow(subTrayPanel, 3);
        grid.Children.Add(subTrayPanel);
    }

    private CheckBox CreateSubTrayCheckBox(ProviderConfig config, string detailName)
    {
        var enabledSubTrays = config.EnabledSubTrays ?? new List<string>();
        var checkBox = new CheckBox
        {
            Content = detailName,
            IsChecked = enabledSubTrays.Contains(detailName, StringComparer.OrdinalIgnoreCase),
            FontSize = 10,
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand,
        };
        checkBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        checkBox.Checked += (_, _) =>
        {
            var trackedConfig = this.GetOrCreateTrackedConfig(config);
            trackedConfig.EnabledSubTrays ??= new List<string>();
            if (!trackedConfig.EnabledSubTrays.Contains(detailName, StringComparer.OrdinalIgnoreCase))
            {
                var enabledSubTrays = trackedConfig.EnabledSubTrays.ToList();
                enabledSubTrays.Add(detailName);
                trackedConfig.EnabledSubTrays = enabledSubTrays;
            }

            this.MarkSettingsChanged(refreshTrayIcons: true);
        };
        checkBox.Unchecked += (_, _) =>
        {
            var trackedConfig = this.GetOrCreateTrackedConfig(config);
            trackedConfig.EnabledSubTrays ??= new List<string>();
            var enabledSubTrays = trackedConfig.EnabledSubTrays.ToList();
            enabledSubTrays.RemoveAll(name => name.Equals(detailName, StringComparison.OrdinalIgnoreCase));
            trackedConfig.EnabledSubTrays = enabledSubTrays;
            this.MarkSettingsChanged(refreshTrayIcons: true);
        };
        return checkBox;
    }

    private TextBox BuildApiKeyEditor(ProviderConfig config)
    {
        var keyBox = new TextBox
        {
            Text = ProviderApiKeyPresentationCatalog.GetDisplayApiKey(config.ApiKey, this._isPrivacyMode),
            Tag = config,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            IsReadOnly = this._isPrivacyMode,
        };

        if (!this._isPrivacyMode)
        {
            keyBox.TextChanged += (s, e) =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.ApiKey = keyBox.Text;
                this.MarkSettingsChanged();
            };
        }

        return keyBox;
    }

    private TextBlock CreateSecondaryStatusText(string text)
    {
        var statusText = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
            Margin = new Thickness(0, 3, 0, 0),
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        return statusText;
    }

    private void RefreshTrayIcons()
    {
        if (Application.Current is App app)
        {
            app.UpdateProviderTrayIcons(this._usages, this._configs, this._preferences);
        }
    }

    private void MarkSettingsChanged(bool refreshTrayIcons = false)
    {
        this.SettingsChanged = true;
        if (refreshTrayIcons)
        {
            this.RefreshTrayIcons();
        }

        this.ScheduleAutoSave();
    }

    private ProviderConfig GetOrCreateTrackedConfig(ProviderConfig config)
    {
        var existing = this._configs.FirstOrDefault(current =>
            current.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        var tracked = this.CloneConfig(config);
        this._configs.Add(tracked);
        return tracked;
    }

    private ProviderConfig CloneConfig(ProviderConfig config)
    {
        return new ProviderConfig
        {
            ProviderId = config.ProviderId,
            ApiKey = config.ApiKey,
            Type = config.Type,
            PlanType = config.PlanType,
            Limit = config.Limit,
            BaseUrl = config.BaseUrl,
            ShowInTray = config.ShowInTray,
            EnableNotifications = config.EnableNotifications,
            EnabledSubTrays = config.EnabledSubTrays.ToList(),
            AuthSource = config.AuthSource,
            Description = config.Description,
            Models = config.Models
                .Select(model => new AIModelConfig
                {
                    Id = model.Id,
                    Name = model.Name,
                    Matches = model.Matches.ToList(),
                    Color = model.Color,
                })
                .ToList(),
        };
    }

    private void ApplyFontPreferenceChange(Action applyChange)
    {
        applyChange();
        this.UpdateFontPreview();
        this.ScheduleAutoSave();
    }

    private async Task<bool> SaveUiPreferencesAsync(bool showErrorDialog = false)
    {
        App.Preferences = this._preferences;
        var saved = await this._preferencesStore.SaveAsync(this._preferences);
        if (!saved)
        {
            this._logger.LogWarning("Failed to save Slim UI preferences");
            if (showErrorDialog)
            {
                MessageBox.Show(
                    "Failed to save Slim UI preferences.",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        return saved;
    }

    private FrameworkElement CreateProviderIcon(string providerId)
    {
        // Map to SVG or create fallback
        var image = new Image();
        image.Source = this.GetProviderImageSource(providerId);
        return image;
    }

    private ImageSource GetProviderImageSource(string providerId)
    {
        try
        {
            var filename = ProviderVisualCatalog.GetIconAssetName(providerId);

            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Try SVG first
            var svgPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.svg");
            if (System.IO.File.Exists(svgPath))
            {
                // Return a simple colored circle as fallback (SVG loading requires SharpVectors)
                return this.CreateFallbackIcon(providerId);
            }

            // Try ICO
            var icoPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.ico");
            if (System.IO.File.Exists(icoPath))
            {
                var icoImage = new System.Windows.Media.Imaging.BitmapImage();
                icoImage.BeginInit();
                icoImage.UriSource = new Uri(icoPath);
                icoImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                icoImage.EndInit();
                icoImage.Freeze();
                return icoImage;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to load provider icon for {ProviderId}", providerId);
        }

        return this.CreateFallbackIcon(providerId);
    }

    private ImageSource CreateFallbackIcon(string providerId)
    {
        // Create a simple colored circle as fallback
        var (color, _) = ProviderVisualCatalog.GetFallbackBadge(providerId, Brushes.Gray);

        // Return a drawing image with just a colored rectangle (simplified)
        var drawing = new GeometryDrawing(
            color,
            new Pen(Brushes.Transparent, 0),
            new RectangleGeometry(new Rect(0, 0, 16, 16)));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private void PopulateLayoutSettings()
    {
        this.AlwaysOnTopCheck.IsChecked = this._preferences.AlwaysOnTop;
        this.AggressiveTopmostCheck.IsChecked = this._preferences.AggressiveAlwaysOnTop;
        this.ForceWin32TopmostCheck.IsChecked = this._preferences.ForceWin32Topmost;
        this.InvertProgressCheck.IsChecked = this._preferences.InvertProgressBar;
        this.InvertCalculationsCheck.IsChecked = this._preferences.InvertCalculations;
        this.ThemeCombo.DisplayMemberPath = nameof(ThemeOption.Label);
        this.ThemeCombo.SelectedValuePath = nameof(ThemeOption.Value);
        this.ThemeCombo.ItemsSource = this.GetThemeOptions();
        this.ThemeCombo.SelectedValue = this._preferences.Theme;

        this.UpdateChannelCombo.ItemsSource = new[]
        {
            new { Label = "Stable", Value = UpdateChannel.Stable },
            new { Label = "Beta", Value = UpdateChannel.Beta },
        };
        this.UpdateChannelCombo.DisplayMemberPath = "Label";
        this.UpdateChannelCombo.SelectedValuePath = "Value";
        this.UpdateChannelCombo.SelectedValue = this._preferences.UpdateChannel;

        this.EnableWindowsNotificationsCheck.IsChecked = this._preferences.EnableNotifications;
        this.NotificationThresholdBox.Text = this._preferences.NotificationThreshold.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        this.NotifyUsageThresholdCheck.IsChecked = this._preferences.NotifyOnUsageThreshold;
        this.NotifyQuotaExceededCheck.IsChecked = this._preferences.NotifyOnQuotaExceeded;
        this.NotifyProviderErrorsCheck.IsChecked = this._preferences.NotifyOnProviderErrors;
        this.EnableQuietHoursCheck.IsChecked = this._preferences.EnableQuietHours;
        this.QuietHoursStartBox.Text = string.IsNullOrWhiteSpace(this._preferences.QuietHoursStart) ? "22:00" : this._preferences.QuietHoursStart;
        this.QuietHoursEndBox.Text = string.IsNullOrWhiteSpace(this._preferences.QuietHoursEnd) ? "07:00" : this._preferences.QuietHoursEnd;
        this.ApplyNotificationControlsState();
        this.YellowThreshold.Text = this._preferences.ColorThresholdYellow.ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.RedThreshold.Text = this._preferences.ColorThresholdRed.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Font settings
        this.PopulateFontComboBox();
        this.FontFamilyCombo.SelectedItem = this._preferences.FontFamily;
        this.FontSizeBox.Text = this._preferences.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.FontBoldCheck.IsChecked = this._preferences.FontBold;
        this.FontItalicCheck.IsChecked = this._preferences.FontItalic;
        this.UpdateFontPreview();
    }

    private IReadOnlyList<ThemeOption> GetThemeOptions()
    {
        return new List<ThemeOption>
        {
            new() { Value = AppTheme.Dark, Label = "Dark" },
            new() { Value = AppTheme.Light, Label = "Light" },
            new() { Value = AppTheme.Corporate, Label = "Corporate" },
            new() { Value = AppTheme.Midnight, Label = "Midnight" },
            new() { Value = AppTheme.Dracula, Label = "Dracula" },
            new() { Value = AppTheme.Nord, Label = "Nord" },
            new() { Value = AppTheme.Monokai, Label = "Monokai" },
            new() { Value = AppTheme.OneDark, Label = "One Dark" },
            new() { Value = AppTheme.SolarizedDark, Label = "Solarized Dark" },
            new() { Value = AppTheme.SolarizedLight, Label = "Solarized Light" },
            new() { Value = AppTheme.CatppuccinFrappe, Label = "Catppuccin Frappe" },
            new() { Value = AppTheme.CatppuccinMacchiato, Label = "Catppuccin Macchiato" },
            new() { Value = AppTheme.CatppuccinMocha, Label = "Catppuccin Mocha" },
            new() { Value = AppTheme.CatppuccinLatte, Label = "Catppuccin Latte" },
        };
    }

    private void PopulateFontComboBox()
    {
        // Get all system fonts
        var fonts = System.Windows.Media.Fonts.GetFontFamilies(new Uri("pack://application:,,,/"))
            .Select(ff => ff.FamilyNames.FirstOrDefault().Value ?? ff.Source)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // If no fonts from pack URI, try alternative method
        if (fonts.Count == 0)
        {
            fonts = System.Windows.Media.Fonts.GetFontFamilies(Environment.GetFolderPath(Environment.SpecialFolder.Fonts))
                .Select(ff => ff.FamilyNames.FirstOrDefault().Value ?? ff.Source)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        // Fallback to common fonts if still empty
        if (fonts.Count == 0)
        {
            fonts = new List<string>
            {
                "Arial", "Calibri", "Cambria", "Comic Sans MS", "Consolas", "Courier New",
                "Georgia", "Helvetica", "Lucida Console", "Segoe UI", "Tahoma", "Times New Roman",
                "Trebuchet MS", "Verdana",
            }.OrderBy(f => f, StringComparer.Ordinal).ToList();
        }

        this.FontFamilyCombo.ItemsSource = fonts;
    }

    private void UpdateFontPreview()
    {
        if (this.FontPreviewText == null)
        {
            return;
        }

        // Update font family
        if (!string.IsNullOrEmpty(this._preferences.FontFamily))
        {
            this.FontPreviewText.FontFamily = new System.Windows.Media.FontFamily(this._preferences.FontFamily);
        }

        // Update font size
        this.FontPreviewText.FontSize = this._preferences.FontSize > 0 ? this._preferences.FontSize : 12;

        // Update font weight
        this.FontPreviewText.FontWeight = this._preferences.FontBold ? FontWeights.Bold : FontWeights.Normal;

        // Update font style
        this.FontPreviewText.FontStyle = this._preferences.FontItalic ? FontStyles.Italic : FontStyles.Normal;
    }

    private void ResetFontBtn_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyFontPreferenceChange(() =>
        {
            this._preferences.FontFamily = "Segoe UI";
            this._preferences.FontSize = 12;
            this._preferences.FontBold = false;
            this._preferences.FontItalic = false;

            this.FontFamilyCombo.SelectedItem = this._preferences.FontFamily;
            this.FontSizeBox.Text = this._preferences.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            this.FontBoldCheck.IsChecked = this._preferences.FontBold;
            this.FontItalicCheck.IsChecked = this._preferences.FontItalic;
        });
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.FontFamilyCombo.SelectedItem is string font)
        {
            this.ApplyFontPreferenceChange(() => this._preferences.FontFamily = font);
        }
    }

    private void FontSizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(this.FontSizeBox.Text, System.Globalization.CultureInfo.InvariantCulture, out int size) && size > 0 && size <= 72)
        {
            this.ApplyFontPreferenceChange(() => this._preferences.FontSize = size);
        }
    }

    private void FontBoldCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        this.ApplyFontPreferenceChange(() => this._preferences.FontBold = this.FontBoldCheck.IsChecked ?? false);
    }

    private void FontItalicCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        this.ApplyFontPreferenceChange(() => this._preferences.FontItalic = this.FontItalicCheck.IsChecked ?? false);
    }

    private async void PrivacyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var newPrivacyMode = !this._isPrivacyMode;
            this._preferences.IsPrivacyMode = newPrivacyMode;
            App.SetPrivacyMode(newPrivacyMode);
            await this.SaveUiPreferencesAsync();
            this.SettingsChanged = true;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "PrivacyBtn_Click failed");
            MessageBox.Show($"Failed to update privacy mode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private async void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            this.ScanBtn.IsEnabled = false;
            this.ScanBtn.Content = "Scanning...";

            var scanResult = await this._monitorService.ScanForKeysAsync();

            if (scanResult.Count > 0)
            {
                MessageBox.Show(
                    $"Found {scanResult.Count} new API key(s). They have been added to your configuration.",
                    "Scan Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                await this.LoadDataAsync();
            }
            else
            {
                MessageBox.Show(
                    "No new API keys found.",
                    "Scan Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to scan for keys: {ex.Message}",
                "Scan Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            this.ScanBtn.IsEnabled = true;
            this.ScanBtn.Content = "Scan for Keys";
        }
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Trigger refresh on agent
            await this._monitorService.TriggerRefreshAsync();

            // Wait a moment for refresh to complete
            await Task.Delay(2000);

            // Reload data
            await this.LoadDataAsync();

            MessageBox.Show(
                "Data refreshed successfully.",
                "Refresh Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to refresh data: {ex.Message}",
                "Refresh Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RefreshHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = await this._monitorService.GetHistoryAsync(100);
            this.HistoryDataGrid.ItemsSource = history;

            if (history.Count == 0)
            {
                MessageBox.Show(
                    "No history data available. The agent may not have collected any data yet.",
                    "No Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load history: {ex.Message}",
                "History Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        this.HistoryDataGrid.ItemsSource = null;
    }

    private async void ExportCsvBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this._monitorService.RefreshPortAsync();
            var csv = await this._monitorService.ExportDataAsync("csv");
            if (string.IsNullOrEmpty(csv))
            {
                MessageBox.Show(
                    "No data to export or Monitor is not running.",
                    "Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"usage_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            };

            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(dialog.FileName, csv);
                MessageBox.Show(
                    $"Exported to {dialog.FileName}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Export failed: {ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportJsonBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this._monitorService.RefreshPortAsync();
            var json = await this._monitorService.ExportDataAsync("json");
            if (string.Equals(json, "[]", StringComparison.Ordinal) || string.IsNullOrEmpty(json))
            {
                MessageBox.Show(
                    "No data to export or Monitor is not running.",
                    "Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"usage_export_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            };

            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(dialog.FileName, json);
                MessageBox.Show(
                    $"Exported to {dialog.FileName}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Export failed: {ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BackupDbBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Database files (*.db)|*.db",
                DefaultExt = ".db",
                FileName = $"usage_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            };

            if (dialog.ShowDialog() == true)
            {
                var dbPath = this._pathProvider.GetDatabasePath();

                if (File.Exists(dbPath))
                {
                    File.Copy(dbPath, dialog.FileName, true);
                    MessageBox.Show(
                        $"Backup saved to {dialog.FileName}",
                        "Backup Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Database file not found.",
                        "Backup Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Backup failed: {ex.Message}",
                "Backup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RestartMonitorBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Kill any running agent process
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("AIUsageTracker.Monitor")
                .Concat(System.Diagnostics.Process.GetProcessesByName("AIUsageTracker.Monitor")))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    this._logger.LogDebug(ex, "Failed to terminate monitor process {ProcessId}", process.Id);
                }
            }

            await Task.Delay(1000);

            // Restart agent
            if (await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(true))
            {
                MessageBox.Show(
                    "Monitor restarted successfully.",
                    "Restart Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "Failed to restart Monitor.",
                    "Restart Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to restart Monitor: {ex.Message}",
                "Restart Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            this.RefreshDiagnosticsLog();
        }
    }

    private async void CheckHealthBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, port) = await this._monitorLifecycleService.IsAgentRunningWithPortAsync().ConfigureAwait(true);
            var healthSnapshot = await this._monitorService.GetHealthSnapshotAsync();
            var status = isRunning ? "Running" : "Not Running";

            MessageBox.Show(
                this.BuildHealthCheckMessage(status, port, healthSnapshot),
                "Health Check",
                MessageBoxButton.OK,
                this.GetHealthCheckIcon(isRunning, healthSnapshot));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to check health: {ex.Message}",
                "Health Check Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            this.RefreshDiagnosticsLog();
        }
    }

    private string BuildHealthCheckMessage(string processStatus, int port, MonitorHealthSnapshot? healthSnapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Monitor Status: {processStatus}");
        builder.AppendLine($"Port: {port}");

        if (healthSnapshot == null)
        {
            return builder.ToString();
        }

        builder.AppendLine($"Service Health: {healthSnapshot.ServiceHealth}");
        builder.AppendLine($"Monitor Version: {healthSnapshot.AgentVersion ?? "unknown"}");
        var contractVersion = healthSnapshot.EffectiveContractVersion ?? "unknown";
        builder.AppendLine($"API Contract: {contractVersion}");
        if (!string.IsNullOrWhiteSpace(healthSnapshot.EffectiveMinClientContractVersion))
        {
            builder.AppendLine($"Min Client Contract: {healthSnapshot.EffectiveMinClientContractVersion}");
        }

        builder.AppendLine($"Last Health Ping: {FormatHealthTimestamp(healthSnapshot.Timestamp)}");
        builder.AppendLine($"Refresh Status: {healthSnapshot.RefreshHealth.Status}");
        builder.AppendLine($"Last Refresh Attempt: {FormatHealthTimestamp(healthSnapshot.RefreshHealth.LastRefreshAttemptUtc)}");
        builder.AppendLine($"Last Successful Refresh: {FormatHealthTimestamp(healthSnapshot.RefreshHealth.LastSuccessfulRefreshUtc)}");
        builder.AppendLine($"Providers In Backoff: {healthSnapshot.RefreshHealth.ProvidersInBackoff}");

        if (healthSnapshot.RefreshHealth.FailingProviders.Count > 0)
        {
            builder.AppendLine($"Failing Providers: {string.Join(", ", healthSnapshot.RefreshHealth.FailingProviders)}");
        }

        if (!string.IsNullOrWhiteSpace(healthSnapshot.RefreshHealth.LastError))
        {
            builder.AppendLine($"Last Refresh Error: {healthSnapshot.RefreshHealth.LastError}");
        }

        return builder.ToString();
    }

    private MessageBoxImage GetHealthCheckIcon(bool isRunning, MonitorHealthSnapshot? healthSnapshot)
    {
        if (!isRunning)
        {
            return MessageBoxImage.Warning;
        }

        return string.Equals(healthSnapshot?.ServiceHealth, "degraded", StringComparison.OrdinalIgnoreCase)
            ? MessageBoxImage.Warning
            : MessageBoxImage.Information;
    }

    private static string FormatHealthTimestamp(DateTime? timestampUtc)
    {
        if (!timestampUtc.HasValue)
        {
            return "Never";
        }

        return $"{timestampUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} (local)";
    }

    private async void ExportDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this._monitorService.RefreshPortAsync();
            await this._monitorService.RefreshAgentInfoAsync();

            var (isRunning, port) = await this._monitorLifecycleService.IsAgentRunningWithPortAsync().ConfigureAwait(true);
            var healthSnapshot = await this._monitorService.GetHealthSnapshotAsync();
            var diagnosticsSnapshot = await this._monitorService.GetDiagnosticsSnapshotAsync();
            var healthDetails = this.SerializeBundlePayload(
                healthSnapshot,
                "Health payload unavailable.");
            var diagnosticsDetails = this.SerializeBundlePayload(
                diagnosticsSnapshot,
                "Diagnostics payload unavailable.");

            var saveDialog = new SaveFileDialog
            {
                FileName = $"ai-usage-tracker-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                AddExtension = true,
            };

            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            var telemetry = MonitorService.GetTelemetrySnapshot();
            var bundle = new StringBuilder();
            bundle.AppendLine("AI Usage Tracker - Diagnostics Bundle");
            bundle.AppendLine($"GeneratedAtUtc: {DateTime.UtcNow:O}");
            bundle.AppendLine($"SlimVersion: {typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "unknown"}");
            bundle.AppendLine($"AgentUrl: {this._monitorService.AgentUrl}");
            bundle.AppendLine($"AgentRunning: {isRunning}");
            bundle.AppendLine($"AgentPort: {port.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Health Summary ===");
            bundle.AppendLine(this.BuildHealthCheckMessage(isRunning ? "Running" : "Not Running", port, healthSnapshot).TrimEnd());
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Health ===");
            bundle.AppendLine(healthDetails);
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Diagnostics ===");
            this.AppendMonitorDiagnosticsSummary(bundle, diagnosticsSnapshot);
            bundle.AppendLine();
            bundle.AppendLine(diagnosticsDetails);
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Errors (monitor.json) ===");
            if (this._monitorService.LastAgentErrors.Count == 0)
            {
                bundle.AppendLine("None");
            }
            else
            {
                foreach (var error in this._monitorService.LastAgentErrors)
                {
                    bundle.AppendLine($"- {error}");
                }
            }

            bundle.AppendLine();

            bundle.AppendLine("=== Slim Telemetry ===");
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "Usage: count={0}, avg={1:F1}ms, last={2}ms, errors={3} ({4:F1}%)\r\n",
                telemetry.UsageRequestCount,
                telemetry.UsageAverageLatencyMs,
                telemetry.UsageLastLatencyMs,
                telemetry.UsageErrorCount,
                telemetry.UsageErrorRatePercent);
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "Refresh: count={0}, avg={1:F1}ms, last={2}ms, errors={3} ({4:F1}%)\r\n",
                telemetry.RefreshRequestCount,
                telemetry.RefreshAverageLatencyMs,
                telemetry.RefreshLastLatencyMs,
                telemetry.RefreshErrorCount,
                telemetry.RefreshErrorRatePercent);
            bundle.AppendLine();

            bundle.AppendLine("=== Slim Diagnostics Log ===");
            var diagnosticsLog = MonitorService.DiagnosticsLog;
            if (diagnosticsLog.Count == 0)
            {
                bundle.AppendLine("No diagnostics captured yet.");
            }
            else
            {
                foreach (var line in diagnosticsLog)
                {
                    bundle.AppendLine(line);
                }
            }

            await File.WriteAllTextAsync(saveDialog.FileName, bundle.ToString());
            MessageBox.Show(
                $"Diagnostics bundle saved to:\n{saveDialog.FileName}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to export diagnostics bundle: {ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            this.RefreshDiagnosticsLog();
        }
    }

    private string SerializeBundlePayload<T>(T? payload, string emptyFallback)
    {
        if (payload == null)
        {
            return emptyFallback;
        }

        return JsonSerializer.Serialize(payload, BundleJsonOptions);
    }

    private void AppendMonitorDiagnosticsSummary(StringBuilder bundle, AgentDiagnosticsSnapshot? diagnostics)
    {
        if (diagnostics == null)
        {
            bundle.AppendLine("Summary unavailable (typed diagnostics not available).");
            return;
        }

        bundle.AppendLine("Summary:");
        bundle.AppendFormat(
            System.Globalization.CultureInfo.InvariantCulture,
            "- Endpoint: port={0}, pid={1}, runtime={2}, args={3}\r\n",
            diagnostics.Port,
            diagnostics.ProcessId,
            diagnostics.Runtime,
            diagnostics.Args.Count);

        if (diagnostics.RefreshTelemetry != null)
        {
            var refresh = diagnostics.RefreshTelemetry;
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "- Refresh telemetry: count={0}, success={1}, failure={2}, error_rate={3:F1}%, avg={4:F1}ms, last={5}ms\r\n",
                refresh.RefreshCount,
                refresh.RefreshSuccessCount,
                refresh.RefreshFailureCount,
                refresh.ErrorRatePercent,
                refresh.AverageLatencyMs,
                refresh.LastLatencyMs);
        }

        if (diagnostics.SchedulerTelemetry != null)
        {
            var scheduler = diagnostics.SchedulerTelemetry;
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "- Scheduler telemetry: queued={0} (h={1}, n={2}, l={3}), recurring={4}, executed={5}, failed={6}, enqueued={7}, dequeued={8}, coalesced_skipped={9}, noop_signals={10}, in_flight={11}\r\n",
                scheduler.TotalQueuedJobs,
                scheduler.HighPriorityQueuedJobs,
                scheduler.NormalPriorityQueuedJobs,
                scheduler.LowPriorityQueuedJobs,
                scheduler.RecurringJobs,
                scheduler.ExecutedJobs,
                scheduler.FailedJobs,
                scheduler.EnqueuedJobs,
                scheduler.DequeuedJobs,
                scheduler.CoalescedSkippedJobs,
                scheduler.DispatchNoopSignals,
                scheduler.InFlightJobs);
        }

        if (diagnostics.PipelineTelemetry != null)
        {
            var pipeline = diagnostics.PipelineTelemetry;
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "- Pipeline telemetry: processed={0}, accepted={1}, rejected={2}, invalid_identity={3}, inactive_filtered={4}, placeholders={5}, detail_adjusted={6}, normalized={7}, privacy_redacted={8}, last_run={9}/{10}\r\n",
                pipeline.TotalProcessedEntries,
                pipeline.TotalAcceptedEntries,
                pipeline.TotalRejectedEntries,
                pipeline.InvalidIdentityCount,
                pipeline.InactiveProviderFilteredCount,
                pipeline.PlaceholderFilteredCount,
                pipeline.DetailContractAdjustedCount,
                pipeline.NormalizedCount,
                pipeline.PrivacyRedactedCount,
                pipeline.LastRunAcceptedEntries,
                pipeline.LastRunTotalEntries);
        }

        if (diagnostics.Observability?.ActivitySourceNames.Count > 0)
        {
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "- Observability: activity_sources={0}\r\n",
                string.Join(", ", diagnostics.Observability.ActivitySourceNames));
        }
    }

    private async Task PersistAllSettingsAsync(bool showErrorDialog)
    {
        if (this._isLoadingSettings)
        {
            return;
        }

        await this._autoSaveSemaphore.WaitAsync();
        try
        {
            if (!this._hasPendingAutoSave && !showErrorDialog)
            {
                return;
            }

            this._hasPendingAutoSave = false;
            this._preferences.AlwaysOnTop = this.AlwaysOnTopCheck.IsChecked ?? true;
            this._preferences.AggressiveAlwaysOnTop = this.AggressiveTopmostCheck.IsChecked ?? false;
            this._preferences.ForceWin32Topmost = this.ForceWin32TopmostCheck.IsChecked ?? false;
            this._preferences.InvertProgressBar = this.InvertProgressCheck.IsChecked ?? false;
            this._preferences.InvertCalculations = this.InvertCalculationsCheck.IsChecked ?? false;
            if (this.ThemeCombo.SelectedValue is AppTheme appTheme)
            {
                this._preferences.Theme = appTheme;
                App.ApplyTheme(appTheme);
            }

            if (this.UpdateChannelCombo.SelectedValue is UpdateChannel channel)
            {
                this._preferences.UpdateChannel = channel;
            }

            if (int.TryParse(this.YellowThreshold.Text, System.Globalization.CultureInfo.InvariantCulture, out var yellow))
            {
                this._preferences.ColorThresholdYellow = yellow;
            }

            if (int.TryParse(this.RedThreshold.Text, System.Globalization.CultureInfo.InvariantCulture, out var red))
            {
                this._preferences.ColorThresholdRed = red;
            }

            if (this.FontFamilyCombo.SelectedItem is string font)
            {
                this._preferences.FontFamily = font;
            }

            if (int.TryParse(this.FontSizeBox.Text, System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 0 && size <= 72)
            {
                this._preferences.FontSize = size;
            }

            this._preferences.FontBold = this.FontBoldCheck.IsChecked ?? false;
            this._preferences.FontItalic = this.FontItalicCheck.IsChecked ?? false;
            this._preferences.IsPrivacyMode = this._isPrivacyMode;

            this._preferences.EnableNotifications = this.EnableWindowsNotificationsCheck.IsChecked ?? false;
            if (double.TryParse(this.NotificationThresholdBox.Text, System.Globalization.CultureInfo.InvariantCulture, out var notifyThreshold))
            {
                this._preferences.NotificationThreshold = Math.Clamp(notifyThreshold, 0, 100);
            }

            this._preferences.NotifyOnUsageThreshold = this.NotifyUsageThresholdCheck.IsChecked ?? true;
            this._preferences.NotifyOnQuotaExceeded = this.NotifyQuotaExceededCheck.IsChecked ?? true;
            this._preferences.NotifyOnProviderErrors = this.NotifyProviderErrorsCheck.IsChecked ?? false;
            this._preferences.EnableQuietHours = this.EnableQuietHoursCheck.IsChecked ?? false;
            this._preferences.QuietHoursStart = this.NormalizeQuietHour(this.QuietHoursStartBox.Text, "22:00");
            this._preferences.QuietHoursEnd = this.NormalizeQuietHour(this.QuietHoursEndBox.Text, "07:00");

            var prefsSaved = await this.SaveUiPreferencesAsync(showErrorDialog);
            if (!prefsSaved)
            {
                return;
            }

            var failedConfigs = new List<string>();
            foreach (var config in this._configs)
            {
                var saved = await this._monitorService.SaveConfigAsync(config);
                if (!saved)
                {
                    failedConfigs.Add(config.ProviderId);
                }
            }

            if (failedConfigs.Count > 0)
            {
                if (showErrorDialog)
                {
                    MessageBox.Show(
                        $"Failed to save provider settings for: {string.Join(", ", failedConfigs)}",
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                return;
            }

            this.RefreshTrayIcons();
            this.SettingsChanged = true;
        }
        finally
        {
            this._autoSaveSemaphore.Release();
        }
    }

    private async void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            this._autoSaveTimer.Stop();
            await this.PersistAllSettingsAsync(showErrorDialog: false);
            this.Close();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "CancelBtn_Click failed");
            MessageBox.Show(
                $"Failed to save settings: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this._isLoadingSettings)
        {
            return;
        }

        if (this.ThemeCombo.SelectedValue is AppTheme appTheme)
        {
            this._preferences.Theme = appTheme;
            App.ApplyTheme(appTheme);
            this.ScheduleAutoSave();
        }
    }

    private void UpdateChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this._isLoadingSettings)
        {
            return;
        }

        if (this.UpdateChannelCombo.SelectedValue is UpdateChannel channel)
        {
            this._preferences.UpdateChannel = channel;
            this.ScheduleAutoSave();
        }
    }

    private void LayoutSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!this.IsInitialized)
        {
            return;
        }

        this.ApplyNotificationControlsState();
        this.ScheduleAutoSave();
    }

    private void LayoutSetting_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!this.IsInitialized)
        {
            return;
        }

        this.ScheduleAutoSave();
    }

    private void EnableWindowsNotificationsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!this.IsInitialized)
        {
            return;
        }

        this.ApplyNotificationControlsState();
        this.ScheduleAutoSave();
    }

    private void ApplyNotificationControlsState()
    {
        if (this.EnableWindowsNotificationsCheck == null)
        {
            return;
        }

        var enabled = this.EnableWindowsNotificationsCheck.IsChecked ?? false;
        if (this.NotificationThresholdBox != null)
        {
            this.NotificationThresholdBox.IsEnabled = enabled;
        }

        if (this.NotifyUsageThresholdCheck != null)
        {
            this.NotifyUsageThresholdCheck.IsEnabled = enabled;
        }

        if (this.NotifyQuotaExceededCheck != null)
        {
            this.NotifyQuotaExceededCheck.IsEnabled = enabled;
        }

        if (this.NotifyProviderErrorsCheck != null)
        {
            this.NotifyProviderErrorsCheck.IsEnabled = enabled;
        }

        if (this.EnableQuietHoursCheck != null)
        {
            this.EnableQuietHoursCheck.IsEnabled = enabled;
        }

        var quietHoursEnabled = enabled && (this.EnableQuietHoursCheck?.IsChecked ?? false);
        if (this.QuietHoursStartBox != null)
        {
            this.QuietHoursStartBox.IsEnabled = quietHoursEnabled;
        }

        if (this.QuietHoursEndBox != null)
        {
            this.QuietHoursEndBox.IsEnabled = quietHoursEnabled;
        }
    }

    private string NormalizeQuietHour(string value, string fallback)
    {
        if (TimeSpan.TryParse(value, out var parsed))
        {
            var normalized = new TimeSpan(parsed.Hours, parsed.Minutes, 0);
            return normalized.ToString("hh\\:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        return fallback;
    }

    private async void SendTestNotificationBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            this.NotificationTestStatusText.Text = "Sending...";

            if (!(this.EnableWindowsNotificationsCheck.IsChecked ?? false))
            {
                this.NotificationTestStatusText.Text = "Enable notifications first.";
                return;
            }

            var result = await this._monitorService.SendTestNotificationDetailedAsync();
            this.NotificationTestStatusText.Text = result.Message;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "SendTestNotificationBtn_Click failed");
            this.NotificationTestStatusText.Text = $"Error: {ex.Message}";
        }
    }
#pragma warning restore VSTHRD100

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }
}
