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
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow : Window
{
    private sealed class ThemeOption
    {
        public AppTheme Value { get; init; }
`n        public string Label { get; init; } = string.Empty;
    }
`n
    private readonly IMonitorService _monitorService;
    private readonly ILogger<SettingsWindow> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly UiPreferencesStore _preferencesStore;
    private List<ProviderConfig> _configs = new();
    private List<ProviderUsage> _usages = new();
    private string? _gitHubAuthUsername;
    private string? _openAiAuthUsername;
    private string? _codexAuthUsername;
    private AppPreferences _preferences = new();
    private AppPreferences _agentPreferences = new();
    private bool _isPrivacyMode = App.IsPrivacyMode;
    private bool _isDeterministicScreenshotMode;
    private bool _isLoadingSettings;
    private bool _hasPendingAutoSave;
    private readonly SemaphoreSlim _autoSaveSemaphore = new(1, 1);
    private readonly DispatcherTimer _autoSaveTimer;

    public bool SettingsChanged { get; private set; }
`n
    public SettingsWindow(IMonitorService monitorService, ILogger<SettingsWindow> logger, UiPreferencesStore preferencesStore, IAppPathProvider pathProvider)
    {
        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;

        InitializeComponent();
        _monitorService = monitorService;
        _logger = logger;
        _pathProvider = pathProvider;
        _preferencesStore = preferencesStore;
        App.PrivacyChanged += OnPrivacyChanged;
        Closed += SettingsWindow_Closed;
        Loaded += SettingsWindow_Loaded;
        UpdatePrivacyButtonState();
    }
`n
    public SettingsWindow() : this(
        App.Host.Services.GetRequiredService<IMonitorService>(),
        App.Host.Services.GetRequiredService<ILogger<SettingsWindow>>(),
        App.Host.Services.GetRequiredService<UiPreferencesStore>(),
        App.Host.Services.GetRequiredService<IAppPathProvider>())
    {
    }
`n
    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _monitorService.RefreshPortAsync();
            await _monitorService.RefreshAgentInfoAsync();
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings load failed");
            MessageBox.Show(
                $"Failed to load Settings: {ex.Message}",
                "Settings Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }
`n
    private async Task LoadDataAsync()
    {
        _isLoadingSettings = true;
        string? loadError = null;

        try
        {
            _isDeterministicScreenshotMode = false;

            _configs = (await _monitorService.GetConfigsAsync().ConfigureAwait(true)).ToList();
            _usages = (await _monitorService.GetUsageAsync().ConfigureAwait(true)).ToList();

            if (_configs.Count == 0)
            {
                loadError = "No providers found. This may indicate:\n" +
                           "- Monitor is not running\n" +
                           "- Failed to connect to Monitor\n" +
                           "- No providers configured in Monitor\n\n" +
                           "Try clicking 'Refresh Data' or restarting the Monitor.";
            }

            _gitHubAuthUsername = await ProviderAuthIdentityDiscovery.TryGetGitHubUsernameAsync(_logger);
            _openAiAuthUsername = await ProviderAuthIdentityDiscovery.TryGetOpenAiUsernameAsync(_logger);
            _codexAuthUsername = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(_logger);
            _preferences = await _preferencesStore.LoadAsync();
            _agentPreferences = await _monitorService.GetPreferencesAsync();
            App.Preferences = _preferences;
            _isPrivacyMode = _preferences.IsPrivacyMode;
            App.SetPrivacyMode(_isPrivacyMode);
            UpdatePrivacyButtonState();

            PopulateProviders();
            RefreshTrayIcons();
            PopulateLayoutSettings();
            await LoadHistoryAsync();
            await UpdateMonitorStatusAsync();
            RefreshDiagnosticsLog();
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
            _isLoadingSettings = false;

            if (loadError != null)
            {
                MessageBox.Show(loadError, "Connection Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
`n
    internal async Task PrepareForHeadlessScreenshotAsync(bool deterministic = false)
    {
        if (deterministic)
        {
            PrepareDeterministicScreenshotData();
        }
        else
        {
            await LoadDataAsync();
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
    }
`n
    internal async Task<IReadOnlyList<string>> CaptureHeadlessTabScreenshotsAsync(string outputDirectory)
    {
        await PrepareForHeadlessScreenshotAsync(deterministic: true);

        var capturedFiles = new List<string>();
        if (MainTabControl.Items.Count == 0)
        {
            const string fallbackName = "screenshot_settings_privacy.png";
            App.RenderWindowContent(this, Path.Combine(outputDirectory, fallbackName));
            capturedFiles.Add(fallbackName);
            return capturedFiles;
        }

        for (var index = 0; index < MainTabControl.Items.Count; index++)
        {
            MainTabControl.SelectedIndex = index;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            var header = (MainTabControl.Items[index] as TabItem)?.Header?.ToString();
            ApplyHeadlessCaptureWindowSize(header);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();

            var tabSlug = BuildTabSlug(header, index);
            var fileName = $"screenshot_settings_{tabSlug}_privacy.png";
            App.RenderWindowContent(this, Path.Combine(outputDirectory, fileName));
            capturedFiles.Add(fileName);
        }

        MainTabControl.SelectedIndex = 0;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();

        const string legacyName = "screenshot_settings_privacy.png";
        App.RenderWindowContent(this, Path.Combine(outputDirectory, legacyName));
        capturedFiles.Add(legacyName);

        return capturedFiles;
    }
`n
    private void ApplyHeadlessCaptureWindowSize(string? tabHeader)
    {
        Width = 600;
        Height = 600;

        if (!_isDeterministicScreenshotMode)
        {
            return;
        }

        if (!string.Equals(tabHeader, "Providers", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Width = 760;
        ProvidersStack.Measure(new Size(Width - 80, double.PositiveInfinity));
        var desiredContentHeight = ProvidersStack.DesiredSize.Height;
        Height = Math.Max(900, Math.Min(3200, desiredContentHeight + 260));
    }
`n
    private void PrepareDeterministicScreenshotData()
    {
        _isDeterministicScreenshotMode = true;
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
        UpdatePrivacyButtonState();

        var fixture = SettingsWindowDeterministicFixture.Create();
        _configs = fixture.Configs;
        _usages = fixture.Usages;

        PopulateProviders();
        PopulateLayoutSettings();

        HistoryDataGrid.ItemsSource = fixture.HistoryRows;

        if (MonitorStatusText != null)
        {
            MonitorStatusText.Text = fixture.MonitorStatusText;
        }

        if (MonitorPortText != null)
        {
            MonitorPortText.Text = fixture.MonitorPortText;
        }

        if (MonitorLogsText != null)
        {
            MonitorLogsText.Text = fixture.MonitorLogsText;
        }
    }
`n
    private static string BuildTabSlug(string? header, int index)
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
`n
    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        App.PrivacyChanged -= OnPrivacyChanged;
    }
`n
    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            _autoSaveTimer.Stop();
            await PersistAllSettingsAsync(showErrorDialog: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoSaveTimer_Tick failed");
        }
    }
`n
    private void ScheduleAutoSave()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _hasPendingAutoSave = true;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }
`n
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
        PopulateProviders();
    }
`n
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
`n
    private async Task UpdateMonitorStatusAsync()
    {
        try
        {
            // Check if agent is running
            var isRunning = await MonitorLauncher.IsAgentRunningAsync();

            // Get the actual port from the agent
            int port = await MonitorLauncher.GetAgentPortAsync();

            if (MonitorStatusText != null)
            {
                MonitorStatusText.Text = isRunning ? "Running" : "Not Running";
            }

            // Update port display
            if (FindName("MonitorPortText") is TextBlock portText)
            {
                portText.Text = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update monitor status");
            if (MonitorStatusText != null)
            {
                MonitorStatusText.Text = "Error";
            }
        }
        finally
        {
            RefreshDiagnosticsLog();
        }
    }
`n
    private void RefreshDiagnosticsLog()
    {
        if (MonitorLogsText == null)
        {
            return;
        }

        if (_isDeterministicScreenshotMode)
        {
            MonitorLogsText.Text = "Monitor health check: OK" + Environment.NewLine +
                                 "Diagnostics available in Settings > Monitor.";
            MonitorLogsText.ScrollToEnd();
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

        MonitorLogsText.Text = string.Join(Environment.NewLine, lines);
        MonitorLogsText.ScrollToEnd();
    }
`n
    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _monitorService.GetHistoryAsync(100);
            HistoryDataGrid.ItemsSource = history;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load history");
        }
    }
`n
    private void PopulateProviders()
    {
        ProvidersStack.Children.Clear();

        var displayItems = ProviderSettingsDisplayCatalog.CreateDisplayItems(_configs, _usages);
        var usageByProviderId = _usages.ToDictionary(usage => usage.ProviderId, StringComparer.OrdinalIgnoreCase);

        foreach (var item in displayItems)
        {
            usageByProviderId.TryGetValue(item.Config.ProviderId, out var usage);
            AddProviderCard(item.Config, usage, item.IsDerived);
        }
    }
`n
    private void AddProviderCard(ProviderConfig config, ProviderUsage? usage, bool isDerived = false)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10, 8, 10, 8)
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Inputs

        var settingsBehavior = ProviderSettingsCatalog.Resolve(config, usage, isDerived);
        var headerPanel = BuildProviderHeader(config, settingsBehavior, isDerived);

        grid.Children.Add(headerPanel);

        // Input row
        var keyPanel = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyContent = BuildProviderInputContent(config, usage, settingsBehavior);
        Grid.SetColumn(keyContent, 0);
        keyPanel.Children.Add(keyContent);

        Grid.SetRow(keyPanel, 1);
        grid.Children.Add(keyPanel);

        var subTrayDetails = ProviderSubTrayCatalog.GetEligibleDetails(usage);

        if (!isDerived && subTrayDetails is { Count: > 0 })
        {
            AddSubTraySection(grid, config, subTrayDetails);
        }

        card.Child = grid;
        ProvidersStack.Children.Add(card);
    }
`n
    private FrameworkElement BuildProviderInputContent(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        return settingsBehavior.InputMode switch
        {
            ProviderInputMode.DerivedReadOnly
                or ProviderInputMode.AntigravityAutoDetected
                or ProviderInputMode.GitHubCopilotAuthStatus
                or ProviderInputMode.OpenAiSessionStatus
                => BuildStatusPanel(config, usage, settingsBehavior),
            _ => BuildApiKeyEditor(config)
        };
    }
`n
    private StackPanel BuildStatusPanel(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage,
            settingsBehavior.InputMode,
            _isPrivacyMode,
            new ProviderAuthIdentities(_gitHubAuthUsername, _openAiAuthUsername, _codexAuthUsername));

        var panel = new StackPanel
        {
            Orientation = presentation.UseHorizontalLayout ? Orientation.Horizontal : Orientation.Vertical
        };

        var statusText = new TextBlock
        {
            Text = presentation.PrimaryText,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontStyle = presentation.PrimaryItalic ? FontStyles.Italic : FontStyles.Normal
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, presentation.PrimaryResourceKey);
        panel.Children.Add(statusText);

        foreach (var line in presentation.SecondaryLines)
        {
            var secondaryText = CreateSecondaryStatusText(line.Text);
            secondaryText.TextWrapping = line.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (line.ExtraTopMargin)
            {
                secondaryText.Margin = new Thickness(0, 4, 0, 0);
            }

            panel.Children.Add(secondaryText);
        }

        return panel;
    }
`n
    private StackPanel BuildProviderHeader(ProviderConfig config, ProviderSettingsBehavior settingsBehavior, bool isDerived)
    {
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

        var icon = CreateProviderIcon(config.ProviderId);
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
            MinWidth = 120
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        headerPanel.Children.Add(title);

        headerPanel.Children.Add(CreateProviderHeaderCheckBox(
            content: "Tray",
            isChecked: config.ShowInTray,
            margin: new Thickness(12, 0, 0, 0),
            isEnabled: !isDerived,
            onCheckedChanged: isChecked =>
            {
                var trackedConfig = GetOrCreateTrackedConfig(config);
                trackedConfig.ShowInTray = isChecked;
                MarkSettingsChanged(refreshTrayIcons: true);
            }));

        headerPanel.Children.Add(CreateProviderHeaderCheckBox(
            content: "Notify",
            isChecked: config.EnableNotifications,
            margin: new Thickness(8, 0, 0, 0),
            isEnabled: !isDerived,
            onCheckedChanged: isChecked =>
            {
                var trackedConfig = GetOrCreateTrackedConfig(config);
                trackedConfig.EnableNotifications = isChecked;
                MarkSettingsChanged();
            }));

        if (settingsBehavior.IsInactive)
        {
            headerPanel.Children.Add(CreateInactiveBadge());
        }

        return headerPanel;
    }
`n
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
            IsEnabled = isEnabled
        };
        checkBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        checkBox.Checked += (_, _) => onCheckedChanged(true);
        checkBox.Unchecked += (_, _) => onCheckedChanged(false);
        return checkBox;
    }
`n
    private static Border CreateInactiveBadge()
    {
        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(205, 92, 92)),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8, 3, 8, 3)
        };

        status.Child = new TextBlock
        {
            Text = "Inactive",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            FontWeight = FontWeights.SemiBold
        };
        return status;
    }
`n
    private void AddSubTraySection(Grid grid, ProviderConfig config, IReadOnlyList<ProviderUsageDetail> subTrayDetails)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8, 0, 8)
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
            Margin = new Thickness(0, 0, 0, 4)
        };
        subTrayTitle.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        subTrayPanel.Children.Add(subTrayTitle);

        foreach (var detail in subTrayDetails)
        {
            subTrayPanel.Children.Add(CreateSubTrayCheckBox(config, detail.Name));
        }

        Grid.SetRow(subTrayPanel, 3);
        grid.Children.Add(subTrayPanel);
    }
`n
    private CheckBox CreateSubTrayCheckBox(ProviderConfig config, string detailName)
    {
        var enabledSubTrays = config.EnabledSubTrays ?? new List<string>();
        var checkBox = new CheckBox
        {
            Content = detailName,
            IsChecked = enabledSubTrays.Contains(detailName, StringComparer.OrdinalIgnoreCase),
            FontSize = 10,
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand
        };
        checkBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        checkBox.Checked += (_, _) =>
        {
            var trackedConfig = GetOrCreateTrackedConfig(config);
            trackedConfig.EnabledSubTrays ??= new List<string>();
            if (!trackedConfig.EnabledSubTrays.Contains(detailName, StringComparer.OrdinalIgnoreCase))
            {
                var enabledSubTrays = trackedConfig.EnabledSubTrays.ToList();
                enabledSubTrays.Add(detailName);
                trackedConfig.EnabledSubTrays = enabledSubTrays;
            }

            MarkSettingsChanged(refreshTrayIcons: true);
        };
        checkBox.Unchecked += (_, _) =>
        {
            var trackedConfig = GetOrCreateTrackedConfig(config);
            trackedConfig.EnabledSubTrays ??= new List<string>();
            var enabledSubTrays = trackedConfig.EnabledSubTrays.ToList();
            enabledSubTrays.RemoveAll(name => name.Equals(detailName, StringComparison.OrdinalIgnoreCase));
            trackedConfig.EnabledSubTrays = enabledSubTrays;
            MarkSettingsChanged(refreshTrayIcons: true);
        };
        return checkBox;
    }
`n
    private TextBox BuildApiKeyEditor(ProviderConfig config)
    {
        var keyBox = new TextBox
        {
            Text = ProviderApiKeyPresentationCatalog.GetDisplayApiKey(config.ApiKey, _isPrivacyMode),
            Tag = config,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            IsReadOnly = _isPrivacyMode
        };

        if (!_isPrivacyMode)
        {
            keyBox.TextChanged += (s, e) =>
            {
                var trackedConfig = GetOrCreateTrackedConfig(config);
                trackedConfig.ApiKey = keyBox.Text;
                MarkSettingsChanged();
            };
        }

        return keyBox;
    }
`n
    private TextBlock CreateSecondaryStatusText(string text)
    {
        var statusText = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
            Margin = new Thickness(0, 3, 0, 0)
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        return statusText;
    }
`n
    private void RefreshTrayIcons()
    {
        if (Application.Current is App app)
        {
            app.UpdateProviderTrayIcons(_usages, _configs, _preferences);
        }
    }
`n
    private void MarkSettingsChanged(bool refreshTrayIcons = false)
    {
        SettingsChanged = true;
        if (refreshTrayIcons)
        {
            RefreshTrayIcons();
        }

        ScheduleAutoSave();
    }
`n
    private ProviderConfig GetOrCreateTrackedConfig(ProviderConfig config)
    {
        var existing = _configs.FirstOrDefault(current =>
            current.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        var tracked = CloneConfig(config);
        _configs.Add(tracked);
        return tracked;
    }
`n
    private static ProviderConfig CloneConfig(ProviderConfig config)
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
                    Color = model.Color
                })
                .ToList()
        };
    }
`n
    private void ApplyFontPreferenceChange(Action applyChange)
    {
        applyChange();
        UpdateFontPreview();
        ScheduleAutoSave();
    }
`n
    private async Task<bool> SaveUiPreferencesAsync(bool showErrorDialog = false)
    {
        App.Preferences = _preferences;
        var saved = await _preferencesStore.SaveAsync(_preferences);
        if (!saved)
        {
            _logger.LogWarning("Failed to save Slim UI preferences");
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
`n
    private FrameworkElement CreateProviderIcon(string providerId)
    {
        // Map to SVG or create fallback
        var image = new Image();
        image.Source = GetProviderImageSource(providerId);
        return image;
    }
`n
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
                return CreateFallbackIcon(providerId);
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
            _logger.LogDebug(ex, "Failed to load provider icon for {ProviderId}", providerId);
        }

        return CreateFallbackIcon(providerId);
    }
`n
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
`n
    private void PopulateLayoutSettings()
    {
        AlwaysOnTopCheck.IsChecked = _preferences.AlwaysOnTop;
        AggressiveTopmostCheck.IsChecked = _preferences.AggressiveAlwaysOnTop;
        ForceWin32TopmostCheck.IsChecked = _preferences.ForceWin32Topmost;
        InvertProgressCheck.IsChecked = _preferences.InvertProgressBar;
        InvertCalculationsCheck.IsChecked = _preferences.InvertCalculations;
        ThemeCombo.DisplayMemberPath = nameof(ThemeOption.Label);
        ThemeCombo.SelectedValuePath = nameof(ThemeOption.Value);
        ThemeCombo.ItemsSource = GetThemeOptions();
        ThemeCombo.SelectedValue = _preferences.Theme;

        UpdateChannelCombo.ItemsSource = new[]
        {
            new { Label = "Stable", Value = UpdateChannel.Stable },
            new { Label = "Beta", Value = UpdateChannel.Beta }
        };
        UpdateChannelCombo.DisplayMemberPath = "Label";
        UpdateChannelCombo.SelectedValuePath = "Value";
        UpdateChannelCombo.SelectedValue = _preferences.UpdateChannel;

        EnableWindowsNotificationsCheck.IsChecked = _agentPreferences.EnableNotifications;
        NotificationThresholdBox.Text = _agentPreferences.NotificationThreshold.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        NotifyUsageThresholdCheck.IsChecked = _agentPreferences.NotifyOnUsageThreshold;
        NotifyQuotaExceededCheck.IsChecked = _agentPreferences.NotifyOnQuotaExceeded;
        NotifyProviderErrorsCheck.IsChecked = _agentPreferences.NotifyOnProviderErrors;
        EnableQuietHoursCheck.IsChecked = _agentPreferences.EnableQuietHours;
        QuietHoursStartBox.Text = string.IsNullOrWhiteSpace(_agentPreferences.QuietHoursStart) ? "22:00" : _agentPreferences.QuietHoursStart;
        QuietHoursEndBox.Text = string.IsNullOrWhiteSpace(_agentPreferences.QuietHoursEnd) ? "07:00" : _agentPreferences.QuietHoursEnd;
        ApplyNotificationControlsState();
        YellowThreshold.Text = _preferences.ColorThresholdYellow.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RedThreshold.Text = _preferences.ColorThresholdRed.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Font settings
        PopulateFontComboBox();
        FontFamilyCombo.SelectedItem = _preferences.FontFamily;
        FontSizeBox.Text = _preferences.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FontBoldCheck.IsChecked = _preferences.FontBold;
        FontItalicCheck.IsChecked = _preferences.FontItalic;
        UpdateFontPreview();
    }
`n
    private static IReadOnlyList<ThemeOption> GetThemeOptions()
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
            new() { Value = AppTheme.CatppuccinLatte, Label = "Catppuccin Latte" }
        };
    }
`n
    private void PopulateFontComboBox()
    {
        // Get all system fonts
        var fonts = System.Windows.Media.Fonts.GetFontFamilies(new Uri("pack://application:,,,/"))
            .Select(ff => ff.FamilyNames.FirstOrDefault().Value ?? ff.Source)
            .OrderBy(f => f)
            .ToList();

        // If no fonts from pack URI, try alternative method
        if (fonts.Count == 0)
        {
            fonts = System.Windows.Media.Fonts.GetFontFamilies(Environment.GetFolderPath(Environment.SpecialFolder.Fonts))
                .Select(ff => ff.FamilyNames.FirstOrDefault().Value ?? ff.Source)
                .OrderBy(f => f)
                .ToList();
        }

        // Fallback to common fonts if still empty
        if (fonts.Count == 0)
        {
            fonts = new List<string>
            {
                "Arial", "Calibri", "Cambria", "Comic Sans MS", "Consolas", "Courier New",
                "Georgia", "Helvetica", "Lucida Console", "Segoe UI", "Tahoma", "Times New Roman",
                "Trebuchet MS", "Verdana"
            }.OrderBy(f => f).ToList();
        }

        FontFamilyCombo.ItemsSource = fonts;
    }
`n
    private void UpdateFontPreview()
    {
        if (FontPreviewText == null) return;

        // Update font family
        if (!string.IsNullOrEmpty(_preferences.FontFamily))
        {
            FontPreviewText.FontFamily = new System.Windows.Media.FontFamily(_preferences.FontFamily);
        }

        // Update font size
        FontPreviewText.FontSize = _preferences.FontSize > 0 ? _preferences.FontSize : 12;

        // Update font weight
        FontPreviewText.FontWeight = _preferences.FontBold ? FontWeights.Bold : FontWeights.Normal;

        // Update font style
        FontPreviewText.FontStyle = _preferences.FontItalic ? FontStyles.Italic : FontStyles.Normal;
    }
`n
    private void ResetFontBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplyFontPreferenceChange(() =>
        {
            _preferences.FontFamily = "Segoe UI";
            _preferences.FontSize = 12;
            _preferences.FontBold = false;
            _preferences.FontItalic = false;

            FontFamilyCombo.SelectedItem = _preferences.FontFamily;
            FontSizeBox.Text = _preferences.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            FontBoldCheck.IsChecked = _preferences.FontBold;
            FontItalicCheck.IsChecked = _preferences.FontItalic;
        });
    }
`n
    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontFamilyCombo.SelectedItem is string font)
        {
            ApplyFontPreferenceChange(() => _preferences.FontFamily = font);
        }
    }
`n
    private void FontSizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(FontSizeBox.Text, System.Globalization.CultureInfo.InvariantCulture, out int size) && size > 0 && size <= 72)
        {
            ApplyFontPreferenceChange(() => _preferences.FontSize = size);
        }
    }
`n
    private void FontBoldCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        ApplyFontPreferenceChange(() => _preferences.FontBold = FontBoldCheck.IsChecked ?? false);
    }
`n
    private void FontItalicCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        ApplyFontPreferenceChange(() => _preferences.FontItalic = FontItalicCheck.IsChecked ?? false);
    }
`n
    private async void PrivacyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var newPrivacyMode = !_isPrivacyMode;
            _preferences.IsPrivacyMode = newPrivacyMode;
            App.SetPrivacyMode(newPrivacyMode);
            await SaveUiPreferencesAsync();
            SettingsChanged = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PrivacyBtn_Click failed");
            MessageBox.Show($"Failed to update privacy mode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
`n
    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
`n
    private async void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScanBtn.IsEnabled = false;
            ScanBtn.Content = "Scanning...";

            var (count, configs) = await _monitorService.ScanForKeysAsync();

            if (count > 0)
            {
                MessageBox.Show($"Found {count} new API key(s). They have been added to your configuration.",
                    "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadDataAsync();
            }
            else
            {
                MessageBox.Show("No new API keys found.",
                    "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to scan for keys: {ex.Message}",
                "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanBtn.IsEnabled = true;
            ScanBtn.Content = "Scan for Keys";
        }
    }
`n
    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Trigger refresh on agent
            await _monitorService.TriggerRefreshAsync();

            // Wait a moment for refresh to complete
            await Task.Delay(2000);

            // Reload data
            await LoadDataAsync();

            MessageBox.Show("Data refreshed successfully.", "Refresh Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to refresh data: {ex.Message}", "Refresh Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
`n
    private async void RefreshHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = await _monitorService.GetHistoryAsync(100);
            HistoryDataGrid.ItemsSource = history;

            if (history.Count == 0)
            {
                MessageBox.Show("No history data available. The agent may not have collected any data yet.",
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load history: {ex.Message}", "History Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
`n
    private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        HistoryDataGrid.ItemsSource = null;
    }
`n
    private async void ExportCsvBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _monitorService.RefreshPortAsync();
            var csv = await _monitorService.ExportDataAsync("csv");
            if (string.IsNullOrEmpty(csv))
            {
                MessageBox.Show("No data to export or Monitor is not running.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"usage_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(dialog.FileName, csv);
                MessageBox.Show($"Exported to {dialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
`n
    private async void ExportJsonBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _monitorService.RefreshPortAsync();
            var json = await _monitorService.ExportDataAsync("json");
            if (string.Equals(json, "[]", StringComparison.Ordinal) || string.IsNullOrEmpty(json))
            {
                MessageBox.Show("No data to export or Monitor is not running.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"usage_export_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(dialog.FileName, json);
                MessageBox.Show($"Exported to {dialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
`n
    private void BackupDbBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Database files (*.db)|*.db",
                DefaultExt = ".db",
                FileName = $"usage_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db"
            };

            if (dialog.ShowDialog() == true)
            {
                var dbPath = _pathProvider.GetDatabasePath();

                if (File.Exists(dbPath))
                {
                    File.Copy(dbPath, dialog.FileName, true);
                    MessageBox.Show($"Backup saved to {dialog.FileName}", "Backup Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Database file not found.", "Backup Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Backup failed: {ex.Message}", "Backup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
`n
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
                    _logger.LogDebug(ex, "Failed to terminate monitor process {ProcessId}", process.Id);
                }
            }

            await Task.Delay(1000);

            // Restart agent
            if (await MonitorLauncher.EnsureAgentRunningAsync())
            {
                MessageBox.Show("Monitor restarted successfully.", "Restart Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to restart Monitor.", "Restart Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to restart Monitor: {ex.Message}", "Restart Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshDiagnosticsLog();
        }
    }
`n
    private async void CheckHealthBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, port) = await MonitorLauncher.IsAgentRunningWithPortAsync();
            var status = isRunning ? "Running" : "Not Running";

            MessageBox.Show($"Monitor Status: {status}\n\nPort: {port}", "Health Check",
                MessageBoxButton.OK, isRunning ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to check health: {ex.Message}", "Health Check Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshDiagnosticsLog();
        }
    }
`n
    private async void ExportDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _monitorService.RefreshPortAsync();
            await _monitorService.RefreshAgentInfoAsync();

            var (isRunning, port) = await MonitorLauncher.IsAgentRunningWithPortAsync();
            var healthDetails = await _monitorService.GetHealthDetailsAsync();
            var diagnosticsDetails = await _monitorService.GetDiagnosticsDetailsAsync();

            var saveDialog = new SaveFileDialog
            {
                FileName = $"ai-usage-tracker-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                AddExtension = true
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
            bundle.AppendLine($"AgentUrl: {_monitorService.AgentUrl}");
            bundle.AppendLine($"AgentRunning: {isRunning}");
            bundle.AppendLine($"AgentPort: {port.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Health ===");
            bundle.AppendLine(FormatJsonForBundle(healthDetails));
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Diagnostics ===");
            bundle.AppendLine(FormatJsonForBundle(diagnosticsDetails));
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Errors (monitor.json) ===");
            if (_monitorService.LastAgentErrors.Count == 0)
            {
                bundle.AppendLine("None");
            }
            else
            {
                foreach (var error in _monitorService.LastAgentErrors)
                {
                    bundle.AppendLine($"- {error}");
                }
            }
            bundle.AppendLine();

            bundle.AppendLine("=== Slim Telemetry ===");
            bundle.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "Usage: count={0}, avg={1:F1}ms, last={2}ms, errors={3} ({4:F1}%)\r\n",
                telemetry.UsageRequestCount, telemetry.UsageAverageLatencyMs, telemetry.UsageLastLatencyMs, telemetry.UsageErrorCount, telemetry.UsageErrorRatePercent);
            bundle.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "Refresh: count={0}, avg={1:F1}ms, last={2}ms, errors={3} ({4:F1}%)\r\n",
                telemetry.RefreshRequestCount, telemetry.RefreshAverageLatencyMs, telemetry.RefreshLastLatencyMs, telemetry.RefreshErrorCount, telemetry.RefreshErrorRatePercent);
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
            MessageBox.Show($"Diagnostics bundle saved to:\n{saveDialog.FileName}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export diagnostics bundle: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshDiagnosticsLog();
        }
    }
`n
    private static string FormatJsonForBundle(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "(empty)";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return content;
        }
    }
`n
    private async Task PersistAllSettingsAsync(bool showErrorDialog)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        await _autoSaveSemaphore.WaitAsync();
        try
        {
            if (!_hasPendingAutoSave && !showErrorDialog)
            {
                return;
            }

            _hasPendingAutoSave = false;
            _preferences.AlwaysOnTop = AlwaysOnTopCheck.IsChecked ?? true;
            _preferences.AggressiveAlwaysOnTop = AggressiveTopmostCheck.IsChecked ?? false;
            _preferences.ForceWin32Topmost = ForceWin32TopmostCheck.IsChecked ?? false;
            _preferences.InvertProgressBar = InvertProgressCheck.IsChecked ?? false;
            _preferences.InvertCalculations = InvertCalculationsCheck.IsChecked ?? false;
            if (ThemeCombo.SelectedValue is AppTheme appTheme)
            {
                _preferences.Theme = appTheme;
                App.ApplyTheme(appTheme);
            }

            if (UpdateChannelCombo.SelectedValue is UpdateChannel channel)
            {
                _preferences.UpdateChannel = channel;
            }

            if (int.TryParse(YellowThreshold.Text, System.Globalization.CultureInfo.InvariantCulture, out var yellow))
            {
                _preferences.ColorThresholdYellow = yellow;
            }

            if (int.TryParse(RedThreshold.Text, System.Globalization.CultureInfo.InvariantCulture, out var red))
            {
                _preferences.ColorThresholdRed = red;
            }

            if (FontFamilyCombo.SelectedItem is string font)
            {
                _preferences.FontFamily = font;
            }

            if (int.TryParse(FontSizeBox.Text, System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 0 && size <= 72)
            {
                _preferences.FontSize = size;
            }

            _preferences.FontBold = FontBoldCheck.IsChecked ?? false;
            _preferences.FontItalic = FontItalicCheck.IsChecked ?? false;
            _preferences.IsPrivacyMode = _isPrivacyMode;

            _agentPreferences.EnableNotifications = EnableWindowsNotificationsCheck.IsChecked ?? false;
            if (double.TryParse(NotificationThresholdBox.Text, System.Globalization.CultureInfo.InvariantCulture, out var notifyThreshold))
            {
                _agentPreferences.NotificationThreshold = Math.Clamp(notifyThreshold, 0, 100);
            }

            _agentPreferences.NotifyOnUsageThreshold = NotifyUsageThresholdCheck.IsChecked ?? true;
            _agentPreferences.NotifyOnQuotaExceeded = NotifyQuotaExceededCheck.IsChecked ?? true;
            _agentPreferences.NotifyOnProviderErrors = NotifyProviderErrorsCheck.IsChecked ?? false;
            _agentPreferences.EnableQuietHours = EnableQuietHoursCheck.IsChecked ?? false;
            _agentPreferences.QuietHoursStart = NormalizeQuietHour(QuietHoursStartBox.Text, "22:00");
            _agentPreferences.QuietHoursEnd = NormalizeQuietHour(QuietHoursEndBox.Text, "07:00");

            var prefsSaved = await SaveUiPreferencesAsync(showErrorDialog);
            if (!prefsSaved)
            {
                return;
            }

            var agentPrefsSaved = await _monitorService.SavePreferencesAsync(_agentPreferences);
            if (!agentPrefsSaved)
            {
                if (showErrorDialog)
                {
                    MessageBox.Show(
                        "Failed to save monitor notification preferences.",
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                return;
            }

            var failedConfigs = new List<string>();
            foreach (var config in _configs)
            {
                var saved = await _monitorService.SaveConfigAsync(config);
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

            RefreshTrayIcons();
            SettingsChanged = true;
        }
        finally
        {
            _autoSaveSemaphore.Release();
        }
    }
`n
    private async void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _autoSaveTimer.Stop();
            await PersistAllSettingsAsync(showErrorDialog: false);
            this.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CancelBtn_Click failed");
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
`n
    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        if (ThemeCombo.SelectedValue is AppTheme appTheme)
        {
            _preferences.Theme = appTheme;
            App.ApplyTheme(appTheme);
            ScheduleAutoSave();
        }
    }
`n
    private void UpdateChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        if (UpdateChannelCombo.SelectedValue is UpdateChannel channel)
        {
            _preferences.UpdateChannel = channel;
            ScheduleAutoSave();
        }
    }
`n
    private void LayoutSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyNotificationControlsState();
        ScheduleAutoSave();
    }
`n
    private void LayoutSetting_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ScheduleAutoSave();
    }
`n
    private void EnableWindowsNotificationsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyNotificationControlsState();
        ScheduleAutoSave();
    }
`n
    private void ApplyNotificationControlsState()
    {
        if (EnableWindowsNotificationsCheck == null)
        {
            return;
        }

        var enabled = EnableWindowsNotificationsCheck.IsChecked ?? false;
        if (NotificationThresholdBox != null)
        {
            NotificationThresholdBox.IsEnabled = enabled;
        }

        if (NotifyUsageThresholdCheck != null)
        {
            NotifyUsageThresholdCheck.IsEnabled = enabled;
        }

        if (NotifyQuotaExceededCheck != null)
        {
            NotifyQuotaExceededCheck.IsEnabled = enabled;
        }

        if (NotifyProviderErrorsCheck != null)
        {
            NotifyProviderErrorsCheck.IsEnabled = enabled;
        }

        if (EnableQuietHoursCheck != null)
        {
            EnableQuietHoursCheck.IsEnabled = enabled;
        }

        var quietHoursEnabled = enabled && (EnableQuietHoursCheck?.IsChecked ?? false);
        if (QuietHoursStartBox != null)
        {
            QuietHoursStartBox.IsEnabled = quietHoursEnabled;
        }

        if (QuietHoursEndBox != null)
        {
            QuietHoursEndBox.IsEnabled = quietHoursEnabled;
        }
    }
`n
    private static string NormalizeQuietHour(string value, string fallback)
    {
        if (TimeSpan.TryParse(value, out var parsed))
        {
            var normalized = new TimeSpan(parsed.Hours, parsed.Minutes, 0);
            return normalized.ToString("hh\\:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        return fallback;
    }
`n
    private async void SendTestNotificationBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NotificationTestStatusText.Text = "Sending...";

            if (!(EnableWindowsNotificationsCheck.IsChecked ?? false))
            {
                NotificationTestStatusText.Text = "Enable notifications first.";
                return;
            }

            var result = await _monitorService.SendTestNotificationDetailedAsync();
            NotificationTestStatusText.Text = result.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendTestNotificationBtn_Click failed");
            NotificationTestStatusText.Text = $"Error: {ex.Message}";
        }
    }
`n
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }
}


