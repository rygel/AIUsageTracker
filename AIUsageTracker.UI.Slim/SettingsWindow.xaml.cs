// <copyright file="SettingsWindow.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow : Window
{
    private static readonly JsonSerializerOptions BundleJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly IMonitorService _monitorService;
    private readonly MonitorLifecycleService _monitorLifecycleService;
    private readonly ILogger<SettingsWindow> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly UiPreferencesStore _preferencesStore;
    private readonly Func<UpdateChannel, GitHubUpdateChecker> _createUpdateChecker;
    private readonly SemaphoreSlim _autoSaveSemaphore = new(1, 1);
    private readonly DispatcherTimer _autoSaveTimer;

    private List<ProviderConfig> _configs = new();
    private List<ProviderUsage> _usages = new();
    private AppPreferences _preferences = new();
    private bool _isPrivacyMode = App.IsPrivacyMode;
    private bool _isDeterministicScreenshotMode;
    private bool _isLoadingSettings;
    private bool _hasPendingAutoSave;
    private UpdateInfo? _pendingUpdate;
    private GitHubUpdateChecker? _pendingUpdateChecker;

    public SettingsWindow(
        IMonitorService monitorService,
        MonitorLifecycleService monitorLifecycleService,
        ILogger<SettingsWindow> logger,
        UiPreferencesStore preferencesStore,
        IAppPathProvider pathProvider,
        Func<UpdateChannel, GitHubUpdateChecker> createUpdateChecker)
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
        this._createUpdateChecker = createUpdateChecker;
        PrivacyChangedWeakEventManager.AddHandler(this.OnPrivacyChanged);
        this.Closing += this.SettingsWindow_Closing;
        this.Closed += this.SettingsWindow_Closed;
        this.Loaded += this.SettingsWindow_Loaded;
        this.UpdatePrivacyButtonState();
    }

    internal bool SettingsChanged { get; private set; }

#pragma warning disable VSTHRD100 // WPF event handlers require async void signatures.
    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await this._monitorService.RefreshPortAsync().ConfigureAwait(true);
            await this._monitorService.RefreshAgentInfoAsync().ConfigureAwait(true);
            await this.LoadDataAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
            this._usages = (await this.GetUsageForDisplayAsync().ConfigureAwait(true)).ToList();

            if (this._configs.Count == 0)
            {
                loadError = "No providers found. This may indicate:\n" +
                           "- Monitor is not running\n" +
                           "- Failed to connect to Monitor\n" +
                           "- No providers configured in Monitor\n\n" +
                           "Try clicking 'Refresh Data' or restarting the Monitor.";
            }

            this._preferences = App.Preferences;
            this._isPrivacyMode = this._preferences.IsPrivacyMode;
            App.SetPrivacyMode(this._isPrivacyMode);
            this.UpdatePrivacyButtonState();

            this.PopulateProviders();
            this.RefreshTrayIcons();
            this.PopulateLayoutSettings();
            this.InitializeCardDesigner();
            await this.LoadHistoryAsync().ConfigureAwait(true);
            await this.UpdateMonitorStatusAsync().ConfigureAwait(true);
            this.RefreshDiagnosticsLog();
        }
        catch (HttpRequestException ex)
        {
            loadError = $"Failed to connect to Monitor: {ex.Message}\n\n" +
                       "Ensure the Monitor is running and accessible.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
            await this.LoadDataAsync().ConfigureAwait(true);
        }

        await this.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.ConfigureAwait(true);
        this.UpdateLayout();
    }

    internal async Task<IReadOnlyList<string>> CaptureHeadlessTabScreenshotsAsync(string outputDirectory)
    {
        await this.PrepareForHeadlessScreenshotAsync(deterministic: true).ConfigureAwait(true);

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
            await this.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.ConfigureAwait(true);

            var header = (this.MainTabControl.Items[index] as TabItem)?.Header?.ToString();
            this.ApplyHeadlessCaptureWindowSize(header);
            await this.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.ConfigureAwait(true);
            this.UpdateLayout();

            var tabSlug = this.BuildTabSlug(header, index);
            var fileName = $"screenshot_settings_{tabSlug}_privacy.png";
            App.RenderWindowContent(this, Path.Combine(outputDirectory, fileName));
            capturedFiles.Add(fileName);
        }

        this.MainTabControl.SelectedIndex = 0;
        await this.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.ConfigureAwait(true);
        this.UpdateLayout();

        return capturedFiles;
    }
#pragma warning restore VSTHRD001

    private async Task<IReadOnlyList<ProviderUsage>> GetUsageForDisplayAsync()
    {
        var groupedSnapshot = await this._monitorService.GetGroupedUsageAsync().ConfigureAwait(true);
        if (groupedSnapshot == null)
        {
            this._logger.LogWarning("Grouped usage snapshot is unavailable.");
            return Array.Empty<ProviderUsage>();
        }

        return GroupedUsageDisplayAdapter.Expand(groupedSnapshot);
    }

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
            ShowUsedPercentages = false,
            ColorThresholdYellow = 60,
            ColorThresholdRed = 80,
            ShowDualQuotaBars = true,
            DualQuotaSingleBarMode = DualQuotaSingleBarMode.Rolling,
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
        this.InitializeCardDesigner();

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

    private async void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Flush any pending auto-save so preferences are persisted to disk
        // regardless of how the window closes (X button, Alt+F4, etc.).
        this._autoSaveTimer.Stop();
        if (this._hasPendingAutoSave)
        {
            await this.PersistAllSettingsAsync(showErrorDialog: false).ConfigureAwait(true);
        }

        // Ensure the main window reloads preferences regardless of how the dialog closes
        // (Close button, X button, or Alt+F4). DialogResult can only be set before the
        // window actually closes, and only when shown via ShowDialog().
        if (!this.DialogResult.HasValue)
        {
            try
            {
                this.DialogResult = true;
            }
            catch (InvalidOperationException)
            {
                // Window was closed via Close() rather than ShowDialog() — e.g., in
                // headless screenshot mode. DialogResult cannot be set; ignore.
            }
        }
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        this._autoSaveTimer.Stop();
        PrivacyChangedWeakEventManager.RemoveHandler(this.OnPrivacyChanged);
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            this._autoSaveTimer.Stop();
            await this.PersistAllSettingsAsync(showErrorDialog: false).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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

    private void ApplyFontPreferenceChange(Action applyChange)
    {
        applyChange();
        this.UpdateFontPreview();
        this.ScheduleAutoSave();
    }

    private async Task<bool> SaveUiPreferencesAsync(bool showErrorDialog = false)
    {
        App.Preferences = this._preferences;
        var saved = await this._preferencesStore.SaveAsync(this._preferences).ConfigureAwait(true);
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

    private void PopulateLayoutSettings()
    {
        this.AlwaysOnTopCheck.IsChecked = this._preferences.AlwaysOnTop;
        this.AggressiveTopmostCheck.IsChecked = this._preferences.AggressiveAlwaysOnTop;
        this.ForceWin32TopmostCheck.IsChecked = this._preferences.ForceWin32Topmost;
        var (monitorAutoStart, uiAutoStart) = WindowsStartupService.Read();
        this.StartMonitorWithWindowsCheck.IsChecked = monitorAutoStart;
        this.StartUiWithWindowsCheck.IsChecked = uiAutoStart;
        this.PopulateDualQuotaBarWindowCombo();
        this.ApplyDisplayModePreference();
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

    private void ApplyDisplayModePreference()
    {
        if (this.ShowUsedPercentagesCheck != null)
        {
            this.ShowUsedPercentagesCheck.IsChecked = this._preferences.PercentageDisplayMode == PercentageDisplayMode.Used;
        }

        if (this.ShowUsagePerHourCheck != null)
        {
            this.ShowUsagePerHourCheck.IsChecked = this._preferences.ShowUsagePerHour;
        }

        if (this.ShowDualQuotaBarsCheck != null)
        {
            this.ShowDualQuotaBarsCheck.IsChecked = this._preferences.ShowDualQuotaBars;
        }

        if (this.DualQuotaBarWindowCombo != null)
        {
            this.DualQuotaBarWindowCombo.SelectedValue = this._preferences.DualQuotaSingleBarMode;
        }

        if (this.EnablePaceAdjustmentCheck != null)
        {
            this.EnablePaceAdjustmentCheck.IsChecked = this._preferences.EnablePaceAdjustment;
        }

        if (this.UseRelativeResetTimeCheck != null)
        {
            this.UseRelativeResetTimeCheck.IsChecked = this._preferences.UseRelativeResetTime;
        }

        this.UpdateDualQuotaControlsState();
    }

    private void PopulateDualQuotaBarWindowCombo()
    {
        if (this.DualQuotaBarWindowCombo == null)
        {
            return;
        }

        this.DualQuotaBarWindowCombo.ItemsSource = new[]
        {
            new { Label = "Weekly (rolling window)", Value = DualQuotaSingleBarMode.Rolling },
            new { Label = "Hourly/burst window", Value = DualQuotaSingleBarMode.Burst },
        };
        this.DualQuotaBarWindowCombo.DisplayMemberPath = "Label";
        this.DualQuotaBarWindowCombo.SelectedValuePath = "Value";
    }

    private void UpdateDualQuotaControlsState()
    {
        if (this.DualQuotaBarWindowCombo == null || this.ShowDualQuotaBarsCheck == null)
        {
            return;
        }

        this.DualQuotaBarWindowCombo.IsEnabled = !(this.ShowDualQuotaBarsCheck.IsChecked ?? true);
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
            await this.SaveUiPreferencesAsync().ConfigureAwait(true);
            this.SettingsChanged = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(ex, "PrivacyBtn_Click failed");
            MessageBox.Show($"Failed to update privacy mode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            this.ScanBtn.IsEnabled = false;
            this.ScanBtn.Content = "Scanning...";

            var scanResult = await this._monitorService.ScanForKeysAsync().ConfigureAwait(true);

            if (scanResult.Count > 0)
            {
                MessageBox.Show(
                    $"Found {scanResult.Count} new API key(s). They have been added to your configuration.",
                    "Scan Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                await this.LoadDataAsync().ConfigureAwait(true);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
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
            await this._monitorService.TriggerRefreshAsync().ConfigureAwait(true);

            // Wait a moment for refresh to complete
            await Task.Delay(2000).ConfigureAwait(true);

            // Reload data
            await this.LoadDataAsync().ConfigureAwait(true);

            MessageBox.Show(
                "Data refreshed successfully.",
                "Refresh Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MessageBox.Show(
                $"Failed to refresh data: {ex.Message}",
                "Refresh Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task PersistAllSettingsAsync(bool showErrorDialog)
    {
        if (this._isLoadingSettings)
        {
            return;
        }

        await this._autoSaveSemaphore.WaitAsync().ConfigureAwait(true);
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
            var startMonitor = this.StartMonitorWithWindowsCheck.IsChecked ?? false;
            var startUi = this.StartUiWithWindowsCheck.IsChecked ?? false;
            this._preferences.StartMonitorWithWindows = startMonitor;
            this._preferences.StartUiWithWindows = startUi;
            WindowsStartupService.Apply(startMonitor, startUi);
            this.ApplyDisplayPreferencesFromControls();
            if (this.ThemeCombo.SelectedValue is AppTheme appTheme)
            {
                this._preferences.Theme = appTheme;
                App.ApplyTheme(appTheme);
            }

            if (this.UpdateChannelCombo.SelectedValue is UpdateChannel channel)
            {
                this._preferences.UpdateChannel = channel;
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

            var prefsSaved = await this.SaveUiPreferencesAsync(showErrorDialog).ConfigureAwait(true);
            if (!prefsSaved)
            {
                return;
            }

            var failedConfigs = new List<string>();
            var removedProviderIds = new List<string>();
            foreach (var config in this._configs)
            {
                var behavior = ResolveProviderSettingsBehavior(
                    config,
                    this._usages.FirstOrDefault(u => string.Equals(u.ProviderId, config.ProviderId, StringComparison.OrdinalIgnoreCase)),
                    isDerived: false);

                if (behavior.InputMode == ProviderInputMode.StandardApiKey && string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    // Suppress re-discovery so the scanner won't re-add the key
                    // from external sources (Roo Code, Kilo Code, env vars).
                    if (!this._preferences.SuppressedProviderIds.Contains(config.ProviderId, StringComparer.OrdinalIgnoreCase))
                    {
                        this._preferences.SuppressedProviderIds.Add(config.ProviderId);
                    }

                    await this._monitorService.RemoveConfigAsync(config.ProviderId).ConfigureAwait(true);
                    removedProviderIds.Add(config.ProviderId);
                    continue;
                }

                // If the user re-adds a key, un-suppress so future scans can update it.
                if (this._preferences.SuppressedProviderIds.Contains(config.ProviderId, StringComparer.OrdinalIgnoreCase))
                {
                    this._preferences.SuppressedProviderIds.Remove(config.ProviderId);
                }

                var saved = await this._monitorService.SaveConfigAsync(config).ConfigureAwait(true);
                if (!saved)
                {
                    failedConfigs.Add(config.ProviderId);
                }
            }

            // Invalidate the ETag cache so the next GetGroupedUsageAsync call
            // fetches fresh data reflecting config changes instead of a stale 304.
            this._monitorService.InvalidateGroupedUsageCache();

            // SuppressedProviderIds was updated in the loop above (after the initial
            // SaveUiPreferencesAsync call). Re-save preferences so the suppression list
            // is actually persisted — otherwise re-discovery on next startup re-adds the key.
            if (removedProviderIds.Count > 0)
            {
                await this._preferencesStore.SaveAsync(this._preferences).ConfigureAwait(true);
            }

            if (removedProviderIds.Count > 0)
            {
                this._configs.RemoveAll(c => removedProviderIds.Contains(c.ProviderId, StringComparer.OrdinalIgnoreCase));
                this.PopulateProviders();
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
            await this.PersistAllSettingsAsync(showErrorDialog: false).ConfigureAwait(true);
            this.DialogResult = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
            this.RenderCardPreview();
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

#pragma warning disable VSTHRD100 // WPF event handlers require async void signatures.
    private async void CheckForUpdatesBtn_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        this.CheckForUpdatesBtn.IsEnabled = false;
        this.UpdateCheckStatus.Text = "Checking...";
        this.UpdateCheckStatus.Foreground = (Brush)this.FindResource("SecondaryText");
        this.DownloadUpdateBtn.Visibility = Visibility.Collapsed;
        this._pendingUpdate = null;
        this._pendingUpdateChecker = null;

        try
        {
            var channel = this._preferences.UpdateChannel;
            var checker = this._createUpdateChecker(channel);
            var update = await checker.CheckForUpdatesAsync().ConfigureAwait(true);

            if (update != null && !string.IsNullOrWhiteSpace(update.Version))
            {
                this.UpdateCheckStatus.Text = $"New version available: {update.Version}";
                this.UpdateCheckStatus.Foreground = (Brush)this.FindResource("ProgressBarGreen");
                this._pendingUpdate = update;
                this._pendingUpdateChecker = checker;
                this.DownloadUpdateBtn.Visibility = Visibility.Visible;
            }
            else
            {
                this.UpdateCheckStatus.Text = "You're up to date.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogWarning(ex, "Manual update check failed");
            this.UpdateCheckStatus.Text = "Check failed. Try again later.";
        }
        finally
        {
            this.CheckForUpdatesBtn.IsEnabled = true;
        }
    }

#pragma warning disable VSTHRD100
    private async void DownloadUpdateBtn_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (this._pendingUpdate == null || this._pendingUpdateChecker == null)
        {
            return;
        }

        var confirmResult = MessageBox.Show(
            $"Download and install version {this._pendingUpdate.Version}?\n\nThe application will restart after installation.",
            "Confirm Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmResult != MessageBoxResult.Yes)
        {
            return;
        }

        this.DownloadUpdateBtn.IsEnabled = false;
        this.CheckForUpdatesBtn.IsEnabled = false;
        this.UpdateCheckStatus.Text = "Downloading...";
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
                Background = (Brush)this.FindResource("Background"),
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Downloading version {this._pendingUpdate.Version}...",
                            Margin = new Thickness(0, 0, 0, 10),
                            Foreground = (Brush)this.FindResource("PrimaryText"),
                        },
                        progressBar,
                    },
                },
            };

            var progress = new Progress<double>(p => progressBar.Value = p);
            progressWindow.Show();

            var result = await this._pendingUpdateChecker.DownloadAndInstallUpdateAsync(this._pendingUpdate, progress).ConfigureAwait(true);
            progressWindow.Close();
            progressWindow = null;

            if (result.Success)
            {
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show(
                    $"Failed to download or install version {this._pendingUpdate.Version}.\n\n" +
                    $"Reason: {result.FailureReason}\n\n" +
                    "Please try again or download manually from the releases page.",
                    "Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                this.UpdateCheckStatus.Text = "Update failed.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progressWindow?.Close();
            this._logger.LogWarning(ex, "Download update failed");
            MessageBox.Show(
                $"Update error: {ex.Message}",
                "Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            this.UpdateCheckStatus.Text = "Update failed.";
        }
        finally
        {
            this.DownloadUpdateBtn.IsEnabled = true;
            this.CheckForUpdatesBtn.IsEnabled = true;
        }
    }

    private void LayoutSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!this.IsInitialized || this._isLoadingSettings)
        {
            return;
        }

        this.UpdateDualQuotaControlsState();
        this.ApplyNotificationControlsState();
        this.ApplyDisplayPreferencesFromControls();
        this.ScheduleAutoSave();
        this.RenderCardPreview();
        this.NotifyMainWindowChanged();
    }

    private void LayoutSetting_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!this.IsInitialized || this._isLoadingSettings)
        {
            return;
        }

        this.ApplyDisplayPreferencesFromControls();
        this.ScheduleAutoSave();
        this.RenderCardPreview();
        this.NotifyMainWindowChanged();
    }

    /// <summary>
    /// Applies display-related control values to preferences so both preview updates
    /// and persisted saves use the same canonical settings path.
    /// </summary>
    private void ApplyDisplayPreferencesFromControls()
    {
        this._preferences.ShowUsedPercentages = this.ShowUsedPercentagesCheck.IsChecked ?? false;
        this._preferences.ShowUsagePerHour = this.ShowUsagePerHourCheck.IsChecked ?? false;
        this._preferences.ShowDualQuotaBars = this.ShowDualQuotaBarsCheck.IsChecked ?? true;
        if (this.DualQuotaBarWindowCombo?.SelectedValue is DualQuotaSingleBarMode dualMode)
        {
            this._preferences.DualQuotaSingleBarMode = dualMode;
        }

        this._preferences.EnablePaceAdjustment = this.EnablePaceAdjustmentCheck.IsChecked ?? true;
        this._preferences.UseRelativeResetTime = this.UseRelativeResetTimeCheck.IsChecked ?? false;

        if (int.TryParse(this.YellowThreshold.Text, System.Globalization.CultureInfo.InvariantCulture, out var yellow))
        {
            this._preferences.ColorThresholdYellow = yellow;
        }

        if (int.TryParse(this.RedThreshold.Text, System.Globalization.CultureInfo.InvariantCulture, out var red))
        {
            this._preferences.ColorThresholdRed = red;
        }
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

            var result = await this._monitorService.SendTestNotificationDetailedAsync().ConfigureAwait(true);
            this.NotificationTestStatusText.Text = result.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(ex, "SendTestNotificationBtn_Click failed");
            this.NotificationTestStatusText.Text = $"Error: {ex.Message}";
        }
    }
#pragma warning restore VSTHRD100

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            this.Close();
        }
    }
}
