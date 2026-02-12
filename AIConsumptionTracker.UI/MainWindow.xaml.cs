using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using System.Threading.Tasks;
using System.Reflection;
using AIConsumptionTracker.Infrastructure.Helpers;
using AIConsumptionTracker.UI.Services;

// =============================================================================
// ⚠️  AI ASSISTANTS: COLOR LOGIC WARNING - SEE GetProgressBarColor() METHOD
// =============================================================================
// Progress bar color logic follows strict design rules documented in DESIGN.md
// DO NOT modify GetProgressBarColor() or any threshold logic without 
// explicit developer approval. Ask the developer first before making changes.
// =============================================================================

namespace AIConsumptionTracker.UI
{
    public partial class MainWindow : Window
    {
        private readonly ProviderManager _providerManager;
        private readonly IConfigLoader _configLoader;
        private readonly INotificationService _notificationService;
        private AppPreferences _preferences = new();
        private List<ProviderUsage> _cachedUsages = new();
        private List<ProviderConfig> _cachedConfigs = new(); // Cache configs for notification checks
        private Dictionary<string, bool> _providerQuotaDepletedState = new(); // Track which providers have depleted quotas
        private int _resetDisplayMode = 0; // 0: Both, 1: Relative Only, 2: Absolute Only
        private readonly System.Windows.Threading.DispatcherTimer _resetTimer;
        private readonly System.Windows.Threading.DispatcherTimer _autoRefreshTimer;
        private readonly System.Windows.Threading.DispatcherTimer _updateCheckTimer;
        private Dictionary<string, ImageSource> _iconCache = new();


        private string GetRelativeTimeString(DateTime? nextReset)
        {
            if (!nextReset.HasValue) return "";
            var diff = nextReset.Value - DateTime.Now;
            if (diff.TotalSeconds <= 0) return "Ready";
            
            if (diff.TotalDays >= 1) return $"{diff.Days}d {diff.Hours}h";
            if (diff.TotalHours >= 1) return $"{diff.Hours}h {diff.Minutes}m";
            return $"{diff.Minutes}m";
        }

        private string FormatResetDisplay(string resetText, DateTime? nextReset)
        {
            if (string.IsNullOrEmpty(resetText)) return resetText;
            
            var relative = GetRelativeTimeString(nextReset);
            
            // Robust absolute time extraction: Find content inside last set of parentheses
            // Example: (Resets: (Feb 05 14:30)) -> Feb 05 14:30
            string absolute;
            var startIdx = resetText.LastIndexOf('(');
            var endIdx = resetText.IndexOf(')', startIdx > -1 ? startIdx : 0);
            if (startIdx >= 0 && endIdx > startIdx)
            {
                absolute = resetText.Substring(startIdx + 1, endIdx - startIdx - 1);
            }
            else
            {
                // Fallback for older formats
                absolute = resetText.Replace("(Resets:", "").Replace(")", "").Trim();
                if (absolute.Contains(" - ")) absolute = absolute.Split(" - ").Last();
            }

            return _resetDisplayMode switch
            {
                1 => string.IsNullOrEmpty(relative) ? "" : $"(Resets: {relative})",
                2 => $"(Resets: {absolute})",
                _ => string.IsNullOrEmpty(relative) ? $"(Resets: {absolute})" : $"(Resets: {relative} - {absolute})" 
            };
        }

        private readonly IUpdateCheckerService _updateChecker;
        private AIConsumptionTracker.Core.Interfaces.UpdateInfo? _latestUpdate;

        public MainWindow(ProviderManager providerManager, IConfigLoader configLoader, IUpdateCheckerService updateChecker, INotificationService notificationService)
        {
            InitializeComponent();
            _providerManager = providerManager;
            _configLoader = configLoader;
            _updateChecker = updateChecker;
            _notificationService = notificationService;

            this.KeyDown += OnKeyDown;

            _resetTimer = new System.Windows.Threading.DispatcherTimer();
            _resetTimer.Interval = TimeSpan.FromSeconds(15);
            _resetTimer.Tick += (s, e) => {
                if (_cachedUsages != null && _cachedUsages.Count > 0)
                {
                    RenderUsages(_cachedUsages);
                }
            };
            _resetTimer.Start();

            _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer();
            _autoRefreshTimer.Tick += async (s, e) => {
                await RefreshData(forceRefresh: true);
            };

            _updateCheckTimer = new System.Windows.Threading.DispatcherTimer();
            _updateCheckTimer.Interval = TimeSpan.FromMinutes(15);
            _updateCheckTimer.Tick += async (s, e) => {
                await CheckForUpdates();
            };
            _updateCheckTimer.Start();
            
            Loaded += async (s, e) => {
                // Position window bottom right (moved from MainWindow_Loaded)
                var desktopWorkingArea = SystemParameters.WorkArea;
                this.Left = desktopWorkingArea.Right - this.Width - 10;
                this.Top = desktopWorkingArea.Bottom - this.Height - 10;

                // Only load if not already set (avoid overwriting what might have been passed)
                if (!_preferencesLoaded)
                {
                    _preferences = await _configLoader.LoadPreferencesAsync();
                    ApplyPreferences();
                    _preferencesLoaded = true;
                }

                var version = Assembly.GetEntryAssembly()?.GetName().Version;
                if (version != null)
                {
                    VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
                }

                _ = CheckForUpdates();
                await RefreshData(forceRefresh: false);

                // Listen for global privacy changes
                if (Application.Current is App app)
                {
                    app.PrivacyChanged += (s, isPrivate) => {
                        _preferences.IsPrivacyMode = isPrivate;
                        ApplyPreferences();
                        UpdatePrivacyButton();
                        RenderUsages(_cachedUsages);
                    };
                }
                UpdatePrivacyButton();
            };


            this.Deactivated += (s, e) => {
                // Only hide if the window is visible and enabled (not showing a modal dialog)
                // AND StayOpen is false
                // AND Preferences are actually loaded (prevent race condition where default false hides it)
                if (this.IsVisible && this.IsEnabled && _preferencesLoaded && !_preferences.StayOpen)
                {
                    // If we have an open child window (Settings), don't hide!
                    foreach (Window win in this.OwnedWindows)
                    {
                        if (win.IsVisible) return;
                    }
                    
                    this.Hide();
                }
            };

            // Subscribe to power mode changes for resume from sleep/hibernate
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private bool _preferencesLoaded = false;

        public void SetInitialPreferences(AppPreferences prefs)
        {
             _preferences = prefs;
             _preferencesLoaded = true;
             ApplyPreferences();
        }

        public async Task PrepareForScreenshot(AppPreferences prefs, List<ProviderUsage> usages)
        {
            SetInitialPreferences(prefs);
            _cachedUsages = usages;
            
            // Ensure version is set
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version != null)
            {
                VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
            
            RenderUsages(usages);
            UpdateLayout();
            UpdatePrivacyButton();
            await Task.Yield(); // Give WPF a moment to pulse layout
        }

        /// <summary>
        /// Handles power mode changes to refresh data when resuming from sleep/hibernate.
        /// This is a core functionality requirement per DESIGN.md.
        /// </summary>
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                // System resumed from sleep/hibernate - refresh data immediately
                Dispatcher.Invoke(async () =>
                {
                    await RefreshData(forceRefresh: true);
                });
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from power mode events to prevent memory leaks
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            base.OnClosed(e);
        }

        private async void PrivacyBtn_Click(object sender, RoutedEventArgs e) => await PrivacyBtn_ClickAsync(sender, e);

        internal async Task PrivacyBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
            {
                await app.TogglePrivacyMode();
            }
            else
            {
                 // Test Fallback: Directly update local state if app is missing
                 _preferences.IsPrivacyMode = !_preferences.IsPrivacyMode;
                 await _configLoader.SavePreferencesAsync(_preferences);
                 ApplyPreferences();
                 UpdatePrivacyButton();
                 RenderUsages(_cachedUsages);
            }
        }

        private SolidColorBrush GetThemeBrush(string resourceKey, Brush fallback)
        {
            var brush = Application.Current?.Resources[resourceKey] as SolidColorBrush;
            return brush ?? (fallback as SolidColorBrush) ?? Brushes.Gray;
        }

        private void UpdatePrivacyButton()
        {
            if (_preferences.IsPrivacyMode)
            {
                PrivacyBtn.SetResourceReference(Button.ForegroundProperty, "PrivacyModeActive");
            }
            else
            {
                PrivacyBtn.SetResourceReference(Button.ForegroundProperty, "PrivacyModeInactive");
            }
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
                        PrivacyBtn_ClickAsync(this, new RoutedEventArgs());
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
            }
        }

        public void ApplyThemeFromPreferences(AppPreferences prefs)
        {
            _preferences.Theme = prefs.Theme;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            // Switch the theme using the centralized helper
            ThemeHelper.ApplyTheme(_preferences);

            // Refresh all UI elements to pick up new colors
            RenderUsages(_cachedUsages);
        }

        private void SwitchTheme(bool isDark)
        {
            ThemeHelper.SwitchTheme(isDark);
        }

        private void ApplyPreferences()
        {
             ShowAllToggle.IsChecked = _preferences.ShowAll;
             StayOpenCheck.IsChecked = _preferences.StayOpen;
             AlwaysOnTopCheck.IsChecked = _preferences.AlwaysOnTop;
             CompactCheck.IsChecked = _preferences.CompactMode;
             this.Topmost = _preferences.AlwaysOnTop;
             ApplyTheme();

             // Apply Font Settings
             try 
             {
                 if (!string.IsNullOrEmpty(_preferences.FontFamily))
                 {
                     this.FontFamily = new FontFamily(_preferences.FontFamily);
                 }
                 if (_preferences.FontSize > 0) this.FontSize = _preferences.FontSize;
                 this.FontWeight = _preferences.FontBold ? FontWeights.Bold : FontWeights.Normal;
                 this.FontStyle = _preferences.FontItalic ? FontStyles.Italic : FontStyles.Normal;
             }
             catch { /* Ignore font errors */ }
             
             // Force redraw to ensure font changes are picked up by existing elements
             this.InvalidateVisual();

             // Update Auto Refresh Timer
             _autoRefreshTimer.Stop();
             if (_preferences.AutoRefreshInterval > 0)
             {
                 _autoRefreshTimer.Interval = TimeSpan.FromSeconds(_preferences.AutoRefreshInterval);
                 _autoRefreshTimer.Start();
             }
        }


        private async void RefreshData_NoArgs(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                await SavePreferences();
                await RefreshData(forceRefresh: true);
            }
        }

        private async void AlwaysOnTop_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                this.Topmost = AlwaysOnTopCheck.IsChecked ?? true;
                await SavePreferences();
            }
        }

        private async void StayOpen_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                await SavePreferences();
            }
        }

        private async void Compact_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                await SavePreferences();
                await RefreshData(forceRefresh: false);
            }
        }

        private async Task SavePreferences()
        {
            _preferences.ShowAll = ShowAllToggle.IsChecked ?? false;
            _preferences.StayOpen = StayOpenCheck.IsChecked ?? false;
            _preferences.AlwaysOnTop = AlwaysOnTopCheck.IsChecked ?? true;
            _preferences.CompactMode = CompactCheck.IsChecked ?? true;
            await _configLoader.SavePreferencesAsync(_preferences);
        }

        public async Task RefreshData(bool forceRefresh = false)
        {
            // Reload preferences to ensure we have the latest settings (e.g. from SettingsWindow)
            _preferences = await _configLoader.LoadPreferencesAsync();
            ApplyPreferences();

            var usages = await _providerManager.GetAllUsageAsync(forceRefresh, OnProviderUsageUpdated);
            _cachedUsages = usages; // Cache the data
            
            // Update Individual Tray Icons - use cached configs to avoid loading again
            var configs = _providerManager.LastConfigs ?? await _configLoader.LoadConfigAsync();
            _cachedConfigs = configs; // Cache configs for notification checks
            if (Application.Current is App app)
            {
                app.UpdateProviderTrayIcons(usages, configs, _preferences);
            }
            
            RenderUsages(usages);
            
            // Check for quota depletion and refresh notifications
            CheckQuotaNotifications(usages, configs);
        }

        private void CheckQuotaNotifications(List<ProviderUsage> usages, List<ProviderConfig> configs)
        {
            // Check global notification setting
            if (!_preferences.EnableNotifications)
                return;

            // Create a lookup for quick config access
            var configLookup = configs.ToDictionary(c => c.ProviderId, c => c);
            
            foreach (var usage in usages)
            {
                if (!usage.IsQuotaBased && usage.PaymentType != PaymentType.Quota)
                    continue;

                var providerId = usage.ProviderId;
                
                // Check if notifications are enabled for this provider
                if (configLookup.TryGetValue(providerId, out var config) && !config.EnableNotifications)
                    continue; // Skip if notifications disabled
                
                var costRemaining = usage.CostLimit - usage.CostUsed;
                var isCurrentlyDepleted = usage.UsagePercentage >= 100 || costRemaining <= 0;
                
                // Check previous state
                bool wasPreviouslyDepleted = _providerQuotaDepletedState.TryGetValue(providerId, out var prevState) && prevState;
                
                if (isCurrentlyDepleted && !wasPreviouslyDepleted)
                {
                    // Quota just got depleted - show notification
                    _notificationService.ShowQuotaExceeded(
                        usage.ProviderName,
                        $"Quota depleted at {usage.UsagePercentage:F1}% usage"
                    );
                    _providerQuotaDepletedState[providerId] = true;
                }
                else if (!isCurrentlyDepleted && wasPreviouslyDepleted)
                {
                    // Quota was refreshed - show notification
                    _notificationService.ShowNotification(
                        $"✅ {usage.ProviderName} Quota Refreshed",
                        $"Your quota has been reset. You now have {costRemaining:F2} {usage.UsageUnit} available.",
                        "showProvider",
                        providerId
                    );
                    _providerQuotaDepletedState[providerId] = false;
                }
                else if (!_providerQuotaDepletedState.ContainsKey(providerId))
                {
                    // First time seeing this provider, record state
                    _providerQuotaDepletedState[providerId] = isCurrentlyDepleted;
                }
            }
            
            // Clean up providers that no longer exist
            var currentProviderIds = usages.Select(u => u.ProviderId).ToHashSet();
            var providersToRemove = _providerQuotaDepletedState.Keys.Where(id => !currentProviderIds.Contains(id)).ToList();
            foreach (var providerId in providersToRemove)
            {
                _providerQuotaDepletedState.Remove(providerId);
            }
        }

        private void OnProviderUsageUpdated(ProviderUsage usage)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateProviderBar(usage);
            });
        }

        private void UpdateProviderBar(ProviderUsage usage)
        {
            // Apply same filtering logic as RenderUsages
            bool showAll = ShowAllToggle?.IsChecked ?? true;
            bool shouldShow = showAll ||
                           (usage.IsAvailable && !usage.Description.Contains("not found", StringComparison.OrdinalIgnoreCase)) ||
                           (usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota || usage.NextResetTime.HasValue || (usage.Details != null && usage.Details.Any(d => d.NextResetTime.HasValue)));

            // Find existing bars for this provider group (search recursively in containers)
            var existingBars = FindElementsByTagRecursive(ProvidersList, usage.ProviderId).ToList();

            if (existingBars.Count > 0)
            {
                // Count mismatch or not showing? Remove all
                int expectedCount = shouldShow ? (1 + (usage.Details?.Count ?? 0)) : 0;
                
                if (existingBars.Count == expectedCount && shouldShow)
                {
                    // Try in-place update for the whole group
                    bool allUpdated = true;
                    
                    // Update parent
                    if (!TryUpdateInPlace(existingBars[0], usage)) allUpdated = false;
                    
                    // Update children
                    if (allUpdated && usage.Details != null)
                    {
                        for (int i = 0; i < usage.Details.Count; i++)
                        {
                            var childUsage = CreateChildUsage(usage, usage.Details[i]);
                            if (!TryUpdateInPlace(existingBars[i + 1], childUsage))
                            {
                                allUpdated = false;
                                break;
                            }
                        }
                    }

                    if (allUpdated) return; 
                }

                // If not showing anymore, or in-place update failed/not possible, remove all existing pieces
                foreach (var bar in existingBars)
                {
                    RemoveElementFromParent(bar);
                }
            }

            // Only add if it should be shown
            if (!shouldShow)
            {
                return;
            }

            // Create new group (Parent + Children)
            var newElements = GetGroupElements(usage);
            
            // Find the appropriate container based on PaymentType
            var targetContainer = GetTargetContainerForProvider(usage);
            if (targetContainer == null)
            {
                // Fall back to ProvidersList if no container found
                targetContainer = ProvidersList;
            }
            
            // Find insertion point to maintain alphabetical order within the container
            int insertIndex = targetContainer.Children.Count;
            for (int i = 0; i < targetContainer.Children.Count; i++)
            {
                if (targetContainer.Children[i] is FrameworkElement fe && fe.Tag != null && fe.Tag.ToString() != usage.ProviderId)
                {
                    // If we found a different provider, check its ID
                    if (string.Compare(fe.Tag.ToString(), usage.ProviderId, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        insertIndex = i;
                        break;
                    }
                }
            }

            // Insert as a group
            for (int i = 0; i < newElements.Count; i++)
            {
                targetContainer.Children.Insert(insertIndex + i, newElements[i]);
            }
        }

        private IEnumerable<FrameworkElement> FindElementsByTagRecursive(Panel parent, string tag)
        {
            foreach (var child in parent.Children)
            {
                if (child is FrameworkElement fe && fe.Tag?.ToString() == tag)
                {
                    yield return fe;
                }
                
                // Recursively search in child panels
                if (child is Panel childPanel)
                {
                    foreach (var result in FindElementsByTagRecursive(childPanel, tag))
                    {
                        yield return result;
                    }
                }
                else if (child is Border border && border.Child is Panel borderPanel)
                {
                    foreach (var result in FindElementsByTagRecursive(borderPanel, tag))
                    {
                        yield return result;
                    }
                }
                else if (child is ContentControl cc && cc.Content is Panel contentPanel)
                {
                    foreach (var result in FindElementsByTagRecursive(contentPanel, tag))
                    {
                        yield return result;
                    }
                }
            }
        }

        private void RemoveElementFromParent(FrameworkElement element)
        {
            if (element.Parent is Panel parent)
            {
                parent.Children.Remove(element);
            }
        }

        private Panel? GetTargetContainerForProvider(ProviderUsage usage)
        {
            // Determine which section container this provider belongs to
            bool isQuota = usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota;
            
            // Find the appropriate container by looking through ProvidersList
            foreach (var child in ProvidersList.Children)
            {
                if (child is StackPanel container && container.Tag is string tag)
                {
                    if (isQuota && tag == "PlansAndQuotasContainer")
                        return container;
                    if (!isQuota && tag == "PayAsYouGoContainer")
                        return container;
                }
            }
            
            return null;
        }

        private bool TryUpdateInPlace(FrameworkElement element, ProviderUsage usage)
        {
            // If display mode changed (Compact vs Standard), we must replace
            bool isCompactElement = element is Grid; // Compact is Grid, Standard is Border
            if (isCompactElement != _preferences.CompactMode) return false;

            // Update Progress Bar
            var progressFill = FindChildByTag<Border>(element, "Part_ProgressFill");
            var progressHost = FindChildByTag<Grid>(element, "Part_ProgressBarHost");
            var background = FindChildByTag<Border>(element, "Part_Background");
            bool shouldHaveProgress = (usage.UsagePercentage > 0 || usage.IsQuotaBased) &&
                                    !usage.Description.Contains("not found", StringComparison.OrdinalIgnoreCase) &&
                                    !usage.Description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);

            if (shouldHaveProgress)
            {
                if (progressFill == null || progressHost == null) return false; // Structure mismatch, need recreate

                if (background != null)
                {
                    background.Visibility = Visibility.Collapsed;
                }
                progressHost.Visibility = Visibility.Visible;

                var indicatorWidth = Math.Min(usage.UsagePercentage, 100);
                if (_preferences.InvertProgressBar) indicatorWidth = Math.Max(0, 100 - indicatorWidth);

                if (progressHost.ColumnDefinitions.Count == 2)
                {
                    progressHost.ColumnDefinitions[0].Width = new GridLength(indicatorWidth, GridUnitType.Star);
                    progressHost.ColumnDefinitions[1].Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star);
                }

                var isQuota = usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota;
                progressFill.Background = GetProgressBarColor(usage.UsagePercentage, isQuota);
            }
            else
            {
                if (progressHost != null)
                {
                    progressHost.Visibility = Visibility.Collapsed;
                }

                var gridElement = element as Grid;
                if (gridElement != null)
                {
                    if (background == null)
                    {
                        background = new Border
                        {
                            Background = GetThemeBrush("CardBackground", Brushes.Gray),
                            CornerRadius = new CornerRadius(0),
                            Tag = "Part_Background"
                        };
                        gridElement.Children.Add(background);
                    }
                    background.Visibility = Visibility.Visible;
                }
            }

            // Update Status Text
            var statusText = FindChildByTag<TextBlock>(element, "Part_StatusText");
            if (statusText != null)
            {
                var newStatus = _preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(usage.Description, usage.AccountName) : usage.Description;
                
                if (usage.PaymentType == PaymentType.Credits)
                {
                    var remaining = usage.CostLimit - usage.CostUsed;
                    newStatus = isCompactElement ? $"{remaining:F2} Rem" : $"{remaining:F2} {usage.UsageUnit} Remaining";
                }
                else if (usage.PaymentType == PaymentType.UsageBased && usage.CostLimit > 0)
                {
                    newStatus = isCompactElement ? $"${usage.CostUsed:F2} / ${usage.CostLimit:F2}" : $"Spent: ${usage.CostUsed:F2} / Limit: ${usage.CostLimit:F2}";
                }

                // Handle embedded reset info
                var rIdx = newStatus.IndexOf("(Resets:");
                if (rIdx >= 0) newStatus = newStatus.Substring(0, rIdx).Trim();

                statusText.Text = newStatus;
                
                // Update color in compact mode if switch from error/missing
                if (isCompactElement)
                {
                    bool isMissing = usage.Description.Contains("not found", StringComparison.OrdinalIgnoreCase);
                    bool isError = usage.Description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);
                    bool isConsoleCheck = usage.Description.Contains("Check Console", StringComparison.OrdinalIgnoreCase);
                    
                    if (isMissing) statusText.SetResourceReference(TextBlock.ForegroundProperty, "StatusTextMissing");
                    else if (isError) statusText.SetResourceReference(TextBlock.ForegroundProperty, "StatusTextError");
                    else if (isConsoleCheck) statusText.SetResourceReference(TextBlock.ForegroundProperty, "StatusTextConsole");
                    else statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                }
            }

            // Update Reset Text
            var resetText = FindChildByTag<TextBlock>(element, "Part_ResetText");
            if (resetText != null)
            {
                var fullDesc = usage.Description;
                var rIdx = fullDesc.IndexOf("(Resets:");
                if (rIdx >= 0)
                {
                    var resetContent = fullDesc.Substring(rIdx);
                    resetText.Text = FormatResetDisplay(resetContent, usage.NextResetTime);
                }
            }

            // Update Name/Account
            var nameText = FindChildByTag<TextBlock>(element, "Part_NameText");
            if (nameText != null)
            {
                var accountPart = string.IsNullOrWhiteSpace(usage.AccountName) ? "" : $" [{(_preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(usage.AccountName, usage.AccountName) : usage.AccountName)}]";
                nameText.Text = _preferences.IsPrivacyMode 
                    ? $"{PrivacyHelper.MaskContent(usage.ProviderName, usage.AccountName)}{accountPart}"
                    : $"{usage.ProviderName}{accountPart}";
            }

            return true;
        }

        private T? FindChildByTag<T>(DependencyObject parent, string tag) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Tag?.ToString() == tag)
                {
                    return element;
                }
                
                var result = FindChildByTag<T>(child, tag);
                if (result != null) return result;
            }
            return null;
        }

        private void RenderUsages(List<ProviderUsage> usages)
        {
            ProvidersList.Children.Clear();
            
            bool showAll = ShowAllToggle?.IsChecked ?? true;
            var filteredUsages = usages
                .Where(u => showAll ||
                           (u.IsAvailable && !u.Description.Contains("not found", StringComparison.OrdinalIgnoreCase)) ||
                           (u.IsQuotaBased || u.PaymentType == PaymentType.Quota || u.NextResetTime.HasValue || (u.Details != null && u.Details.Any(d => d.NextResetTime.HasValue))) ||
                           (!string.IsNullOrEmpty(u.ConfigKey))) // Show if it has a key configured
                .OrderBy(u => u.ProviderName)
                .ToList();

            if (!filteredUsages.Any())
            {
                ProvidersList.Children.Add(new TextBlock 
                { 
                    Text = showAll ? "No providers found." : "No active providers. Toggle 'Show All' to see more.", 
                    Foreground = Brushes.Gray, 
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    Margin = new Thickness(0,20,0,0),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                });
                return;
            }

            var planItems = filteredUsages.Where(u => u.PaymentType == PaymentType.Quota).ToList();
            var payGoItems = filteredUsages.Where(u => u.PaymentType != PaymentType.Quota).ToList();

            if (planItems.Any())
            {
                var (header, container) = CreateCollapsibleGroupHeader("Plans & Quotas", Brushes.DeepSkyBlue, "PlansAndQuotas", 
                    () => _preferences.IsPlansAndQuotasCollapsed,
                    v => _preferences.IsPlansAndQuotasCollapsed = v);
                ProvidersList.Children.Add(header);
                ProvidersList.Children.Add(container);
                RenderGroup(planItems, container);
            }

            if (payGoItems.Any())
            {
                // Add a bit of spacer before the next group if planItems existed
                if (planItems.Any()) ProvidersList.Children.Add(new Border { Height = 12 });
                
                var (header, container) = CreateCollapsibleGroupHeader("Pay As You Go", Brushes.MediumSeaGreen, "PayAsYouGo",
                    () => _preferences.IsPayAsYouGoCollapsed,
                    v => _preferences.IsPayAsYouGoCollapsed = v);
                ProvidersList.Children.Add(header);
                ProvidersList.Children.Add(container);
                RenderGroup(payGoItems, container);
            }
        }

        private void RenderGroup(List<ProviderUsage> groupUsages, StackPanel? container = null)
        {
            var target = container ?? ProvidersList;
            foreach (var usage in groupUsages)
            {
                var elements = GetGroupElements(usage);
                foreach (var el in elements)
                {
                    target.Children.Add(el);
                }
            }
        }

        private List<UIElement> GetGroupElements(ProviderUsage usage)
        {
            var elements = new List<UIElement>();
            
            // Parent
            var parentBar = CreateProviderBar(usage);
            if (parentBar is FrameworkElement fe) fe.Tag = usage.ProviderId;
            elements.Add(parentBar);

            // Children (Details)
            if (usage.Details != null && usage.Details.Count > 0)
            {
                // Special handling for Antigravity - make children collapsible
                if (usage.ProviderId.Equals("antigravity", StringComparison.OrdinalIgnoreCase))
                {
                    var (header, container) = CreateCollapsibleSubHeader("Sub-providers", Brushes.Gold,
                        () => _preferences.IsAntigravityCollapsed,
                        v => _preferences.IsAntigravityCollapsed = v);
                    
                    elements.Add(header);
                    elements.Add(container);
                    
                    foreach (var detail in usage.Details)
                    {
                        var childUsage = CreateChildUsage(usage, detail);
                        var childBar = CreateProviderBar(childUsage, isChild: true);
                        if (childBar is FrameworkElement cfe) cfe.Tag = usage.ProviderId;
                        container.Children.Add(childBar);
                    }
                }
                else
                {
                    foreach (var detail in usage.Details)
                    {
                        var childUsage = CreateChildUsage(usage, detail);
                        var childBar = CreateProviderBar(childUsage, isChild: true);
                        if (childBar is FrameworkElement cfe) cfe.Tag = usage.ProviderId;
                        elements.Add(childBar);
                    }
                }
            }
            return elements;
        }

        private ProviderUsage CreateChildUsage(ProviderUsage parent, ProviderUsageDetail detail)
        {
            double pct = 0;
            if (double.TryParse(detail.Used.TrimEnd('%'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p)) 
                pct = p;

            return new ProviderUsage 
            {
                ProviderId = parent.ProviderId,
                ProviderName = detail.Name,
                Description = detail.Description,
                UsagePercentage = pct,
                IsQuotaBased = true, // Treat as quota bar usually
                IsAvailable = true,
                AccountName = "", // Don't repeat account
                NextResetTime = detail.NextResetTime,
                PaymentType = parent.PaymentType // Inherit for rendering logic
            };
        }

        private UIElement CreateGroupHeader(string title, Brush accent)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var text = new TextBlock
            {
                Text = title.ToUpper(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = accent,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 1.0
            };

            var line = new Border
            {
                Height = 1,
                Background = accent,
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center
            };

            grid.Children.Add(text);
            Grid.SetColumn(line, 1);
            grid.Children.Add(line);

            return grid;
        }

        private (UIElement Header, StackPanel Container) CreateCollapsibleGroupHeader(
            string title, Brush accent, string groupKey, 
            Func<bool> getCollapsed, Action<bool> setCollapsed)
        {
            // Create header grid with toggle button
            var header = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Toggle button (▼/▶)
            var toggleText = new TextBlock
            {
                Text = getCollapsed() ? "▶" : "▼",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = accent,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 1.0,
                Tag = "ToggleIcon"
            };

            // Title text
            var titleText = new TextBlock
            {
                Text = title.ToUpper(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = accent,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 1.0
            };

            // Separator line
            var line = new Border
            {
                Height = 1,
                Background = accent,
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Create container for group items
            var container = new StackPanel();
            container.Tag = $"{groupKey}Container";
            container.Visibility = getCollapsed() ? Visibility.Collapsed : Visibility.Visible;

            // Make the whole header clickable
            header.Cursor = System.Windows.Input.Cursors.Hand;
            header.MouseLeftButtonDown += async (s, e) => 
            {
                var newState = !getCollapsed();
                setCollapsed(newState);
                container.Visibility = newState ? Visibility.Collapsed : Visibility.Visible;
                toggleText.Text = newState ? "▶" : "▼";
                await _configLoader.SavePreferencesAsync(_preferences);
            };

            Grid.SetColumn(toggleText, 0);
            Grid.SetColumn(titleText, 1);
            Grid.SetColumn(line, 2);

            header.Children.Add(toggleText);
            header.Children.Add(titleText);
            header.Children.Add(line);

            return (header, container);
        }

        private (UIElement Header, StackPanel Container) CreateCollapsibleSubHeader(
            string title, Brush accent, 
            Func<bool> getCollapsed, Action<bool> setCollapsed)
        {
            // Create sub-header for nested collapsible sections (indented)
            var header = new Grid { Margin = new Thickness(20, 2, 0, 2) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Toggle button (▼/▶)
            var toggleText = new TextBlock
            {
                Text = getCollapsed() ? "▶" : "▼",
                FontSize = 9,
                Foreground = accent,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 1.0
            };

            // Title text
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 9,
                Foreground = accent,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 1.0
            };

            // Separator line
            var line = new Border
            {
                Height = 1,
                Background = accent,
                Opacity = 0.4,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Create container for child items
            var container = new StackPanel();
            container.Visibility = getCollapsed() ? Visibility.Collapsed : Visibility.Visible;

            // Make the whole header clickable
            header.Cursor = System.Windows.Input.Cursors.Hand;
            header.MouseLeftButtonDown += async (s, e) => 
            {
                var newState = !getCollapsed();
                setCollapsed(newState);
                container.Visibility = newState ? Visibility.Collapsed : Visibility.Visible;
                toggleText.Text = newState ? "▶" : "▼";
                await _configLoader.SavePreferencesAsync(_preferences);
            };

            Grid.SetColumn(toggleText, 0);
            Grid.SetColumn(titleText, 1);
            Grid.SetColumn(line, 2);

            header.Children.Add(toggleText);
            header.Children.Add(titleText);
            header.Children.Add(line);

            return (header, container);
        }


        private UIElement CreateCompactItem(ProviderUsage usage, bool isChild = false)
        {
            bool isMissing = usage.Description.Contains("not found", StringComparison.OrdinalIgnoreCase);
            bool isConsoleCheck = usage.Description.Contains("Check Console", StringComparison.OrdinalIgnoreCase);
            bool isError = usage.Description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);

            // Main Grid Container
            var grid = new Grid
            {
                Margin = new Thickness(isChild ? 20 : 0, 0, 0, 2),
                Height = 24,
                Background = Brushes.Transparent, // Ensure hit-testing works
                Tag = usage.ProviderId
            };

            bool shouldHaveProgress = (usage.UsagePercentage > 0 || usage.IsQuotaBased) && !isMissing && !isError;

            var pGrid = new Grid { Tag = "Part_ProgressBarHost" };
            var indicatorWidth = Math.Min(usage.UsagePercentage, 100);
            if (_preferences.InvertProgressBar) indicatorWidth = Math.Max(0, 100 - indicatorWidth);

            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indicatorWidth, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star) });

            var isQuota2 = usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota;
            var fill = new Border
            {
                Background = GetProgressBarColor(usage.UsagePercentage, isQuota2),
                Opacity = 0.45,
                CornerRadius = new CornerRadius(0),
                Tag = "Part_ProgressFill"
            };
            pGrid.Children.Add(fill);
            pGrid.Visibility = shouldHaveProgress ? Visibility.Visible : Visibility.Collapsed;
            grid.Children.Add(pGrid);

            var bg = new Border
            {
                Background = GetThemeBrush("CardBackground", Brushes.Gray),
                CornerRadius = new CornerRadius(0),
                Tag = "Part_Background",
                Visibility = shouldHaveProgress ? Visibility.Collapsed : Visibility.Visible
            };
            grid.Children.Add(bg);

            // Layer 2: Content Overlay
            var contentPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 6, 0) };

            // Icon (Only for parent, or maybe small dot for child?)
            if (!isChild)
            {
                var icon = new Image
                {
                    Source = GetIconForProvider(usage.ProviderId),
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 1.0,
                    Tag = "Part_Icon"
                };
                contentPanel.Children.Add(icon);
                DockPanel.SetDock(icon, Dock.Left);
            }
            else
            {
                // Indentation spacer/icon for child
                var icon = new Border
                {
                    Width = 4, Height = 4, Background = GetThemeBrush("SecondaryText", Brushes.Gray), CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center
                };
                contentPanel.Children.Add(icon);
                DockPanel.SetDock(icon, Dock.Left);
            }

            // Right Side: Usage/Status (Added first so it's prioritized in limited space)
            var statusText = "";
            string resetText = "";
            Brush statusBrush = GetThemeBrush("SecondaryText", Brushes.Gray);

            if (isMissing) { statusText = "Key Missing"; statusBrush = Brushes.IndianRed; }
            else if (isError) { statusText = "Error"; statusBrush = Brushes.Red; }
            else if (isConsoleCheck) { statusText = "Check Console"; statusBrush = Brushes.Orange; }
            else 
            { 
                statusText = _preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(usage.Description, usage.AccountName) : usage.Description;
                
                // Tailor description based on PaymentType if needed
                if (usage.PaymentType == PaymentType.Credits)
                {
                    var remaining = usage.CostLimit - usage.CostUsed;
                    statusText = $"{remaining:F2} Rem";
                }
                else if (usage.PaymentType == PaymentType.UsageBased && usage.CostLimit > 0)
                {
                    statusText = $"${usage.CostUsed:F2} / ${usage.CostLimit:F2}";
                }

                var rIdx = statusText.IndexOf("(Resets:");
                if (rIdx >= 0)
                {
                    resetText = statusText.Substring(rIdx);
                    statusText = statusText.Substring(0, rIdx).Trim();
                }
            }

            if (!string.IsNullOrEmpty(resetText))
            {
                var resetBlock = new TextBlock
                {
                    Text = FormatResetDisplay(resetText, usage.NextResetTime),
                    FontSize = 10,
                    Foreground = GetThemeBrush("StatusTextWarning", Brushes.Gray),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                    Tag = "Part_ResetText"
                };
                contentPanel.Children.Add(resetBlock);
                DockPanel.SetDock(resetBlock, Dock.Right);
            }

            grid.MouseDown += (s, e) => {
                _resetDisplayMode = (_resetDisplayMode + 1) % 3;
                Dispatcher.BeginInvoke(new Action(() => RenderUsages(_cachedUsages)));
            };

            var rightBlock = new TextBlock
            {
                Text = statusText,
                FontSize = 10,
                Foreground = statusBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Tag = "Part_StatusText"
            };
            contentPanel.Children.Add(rightBlock);
            DockPanel.SetDock(rightBlock, Dock.Right);

            // Name (Added last, gets remaining space)
            var accountPart = string.IsNullOrWhiteSpace(usage.AccountName) ? "" : $" [{(_preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(usage.AccountName, usage.AccountName) : usage.AccountName)}]";
            var nameBlock = new TextBlock
            {
                Text = _preferences.IsPrivacyMode 
                    ? $"{PrivacyHelper.MaskContent(usage.ProviderName)}{accountPart}"
                    : $"{usage.ProviderName}{accountPart}",
                FontWeight = isChild ? FontWeights.Normal : FontWeights.SemiBold,
                FontSize = 11,
                Foreground = isMissing ? GetThemeBrush("TertiaryText", Brushes.Gray) : GetThemeBrush("PrimaryText", Brushes.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = _preferences.IsPrivacyMode 
                    ? $"{PrivacyHelper.MaskContent(usage.ProviderName, usage.AccountName)}{accountPart}"
                    : (string.IsNullOrEmpty(usage.AuthSource) ? $"{usage.ProviderName}{accountPart}" : usage.AuthSource),
                Tag = "Part_NameText"
            };
            contentPanel.Children.Add(nameBlock);
            DockPanel.SetDock(nameBlock, Dock.Left);

            grid.Children.Add(contentPanel);
            
            // Add detailed tooltip with rate limit information if available
            if (usage.Details != null && usage.Details.Any())
            {
                var tooltipBuilder = new System.Text.StringBuilder();
                tooltipBuilder.AppendLine($"{usage.ProviderName}");
                tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
                if (!string.IsNullOrEmpty(usage.Description))
                {
                    tooltipBuilder.AppendLine($"Description: {usage.Description}");
                }
                tooltipBuilder.AppendLine();
                tooltipBuilder.AppendLine("Rate Limits:");
                foreach (var detail in usage.Details)
                {
                    tooltipBuilder.AppendLine($"  {detail.Name}: {detail.Used}");
                }
                
                // Add warning indicator
                if (usage.UsagePercentage >= 90)
                {
                    tooltipBuilder.AppendLine();
                    tooltipBuilder.AppendLine("⚠️ CRITICAL: Approaching rate limit!");
                }
                else if (usage.UsagePercentage >= 70)
                {
                    tooltipBuilder.AppendLine();
                    tooltipBuilder.AppendLine("⚠️ WARNING: High usage");
                }
                
                grid.ToolTip = tooltipBuilder.ToString().Trim();
            }
            else if (!string.IsNullOrEmpty(usage.AccountName) && usage.AccountName.StartsWith("⚠️"))
            {
                // Handle warning message in AccountName
                grid.ToolTip = usage.AccountName;
            }
            
            return grid;
        }

        private UIElement CreateStandardItem(ProviderUsage usage, bool isChild = false)
        {
            bool isMissing = usage.Description.Contains("not found", StringComparison.OrdinalIgnoreCase);
            bool isConsoleCheck = usage.Description.Contains("Check Console", StringComparison.OrdinalIgnoreCase);
            bool isError = usage.Description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);

            // Main Container
            var container = new Border
            {
                Background = GetThemeBrush("CardBackground", Brushes.Gray),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(12),
                Margin = new Thickness(isChild ? 20 : 0, 0, 0, 8),
                BorderBrush = isMissing || isError ? Brushes.Maroon : (isConsoleCheck ? Brushes.DarkOrange : GetThemeBrush("CardBorder", Brushes.Gray)),
                BorderThickness = new Thickness(1),
                Opacity = (isMissing || !usage.IsAvailable) ? 0.6 : 1.0, Tag = usage.ProviderId
            };

            // Special case for non-quota Child Items (e.g. Free Tier: Yes)
            if (isChild && (!usage.IsQuotaBased && usage.UsagePercentage <= 0.001))
            {
               var simpleGrid = new Grid();
               simpleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
               simpleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

               var nameTxt = new TextBlock
               {
                   Text = _preferences.IsPrivacyMode ? PrivacyHelper.MaskString(usage.ProviderName) : usage.ProviderName, // Actually the "Name" of detail
                   Foreground = GetThemeBrush("TertiaryText", Brushes.Gray),
                   FontSize = 12,
                   VerticalAlignment = VerticalAlignment.Center
               };
               
               // Indent
               var panel = new StackPanel { Orientation = Orientation.Horizontal };
               panel.Children.Add(new Border { Width=6, Height=6, Background=GetThemeBrush("SecondaryText", Brushes.Gray), CornerRadius=new CornerRadius(3), Margin=new Thickness(4,0,12,0), VerticalAlignment=VerticalAlignment.Center });
               panel.Children.Add(nameTxt);

               var valueTxt = new TextBlock
               {
                   Text = _preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(usage.Description, usage.AccountName) : usage.Description,
                   Foreground = GetThemeBrush("PrimaryText", Brushes.Gray),
                   FontSize = 12,
                   FontWeight = FontWeights.SemiBold,
                   VerticalAlignment = VerticalAlignment.Center,
                   Margin = new Thickness(10,0,0,0)
               };

               simpleGrid.Children.Add(panel);
               
               Grid.SetColumn(valueTxt, 1);
               simpleGrid.Children.Add(valueTxt);

               container.Child = simpleGrid;
               container.Padding = new Thickness(12, 8, 12, 8); // Tighter padding
               return container;
            }

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name & Account
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bar & Usage Detail

            // Header Row (Row 0): [Icon] Name [Account]
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Label (Missing/Console etc)

                var icon = new Image
                {
                    Source = GetIconForProvider(usage.ProviderId),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 1.0,
                    Tag = "Part_Icon"
                };
                if (!isChild)
                {
                    headerGrid.Children.Add(icon);
                }
                else
                {
                    // Child Indent
                     var indent = new Border { Width=6, Height=6, Background=GetThemeBrush("SecondaryText", Brushes.Gray), CornerRadius=new CornerRadius(3), Margin=new Thickness(4,0,12,0), VerticalAlignment=VerticalAlignment.Center };
                     headerGrid.Children.Add(indent);
                }

                var accountPart = string.IsNullOrWhiteSpace(usage.AccountName) ? "" : $" [{(_preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(usage.AccountName, usage.AccountName) : usage.AccountName)}]";
                var nameBlock = new TextBlock 
                { 
                    Text = _preferences.IsPrivacyMode 
                        ? $"{PrivacyHelper.MaskContent(usage.ProviderName, usage.AccountName)}{accountPart}"
                        : $"{usage.ProviderName}{accountPart}",
                    FontWeight = isChild ? FontWeights.Normal : FontWeights.SemiBold, 
                    FontSize = 13,
                    Foreground = isMissing ? GetThemeBrush("TertiaryText", Brushes.Gray) : GetThemeBrush("PrimaryText", Brushes.Gray),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = _preferences.IsPrivacyMode ? null : (string.IsNullOrEmpty(usage.AuthSource) ? null : usage.AuthSource),
                    Tag = "Part_NameText"
                };
            Grid.SetColumn(nameBlock, 1);
            headerGrid.Children.Add(nameBlock);

            if (isMissing || isConsoleCheck || isError)
            {
                var statusText = isMissing ? "API Key not found" : (isConsoleCheck ? "Check Console" : "[Error]");
                var statusBrush = isMissing ? Brushes.IndianRed : (isConsoleCheck ? Brushes.Orange : Brushes.Red);
                var statusBlock = new TextBlock { Text = statusText, Foreground = statusBrush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Tag = "Part_MainStatusLabel" };
                Grid.SetColumn(statusBlock, 2);
                headerGrid.Children.Add(statusBlock);
            }

            grid.Children.Add(headerGrid);

            // Progress & Details Row (Row 1)
            var usageDetailGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            usageDetailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Progress Bar
            usageDetailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Values/Text

            bool shouldHaveProgress = (usage.UsagePercentage > 0 || usage.IsQuotaBased) && !isMissing && !isError;

            var pGrid = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Tag = "Part_ProgressBarHost" };
            pGrid.Children.Add(new Border { Background = GetThemeBrush("ProgressBarBackground", Brushes.Gray), CornerRadius = new CornerRadius(0) });

            var indicatorWidth = Math.Min(usage.UsagePercentage, 100);
            if (_preferences.InvertProgressBar) indicatorWidth = Math.Max(0, 100 - indicatorWidth);

            var fillGrid = new Grid();
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indicatorWidth, GridUnitType.Star) });
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star) });

            var isQuota3 = usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota;
            var fill = new Border
            {
                Background = GetProgressBarColor(usage.UsagePercentage, isQuota3),
                CornerRadius = new CornerRadius(0),
                Tag = "Part_ProgressFill"
            };
            fillGrid.Children.Add(fill);
            pGrid.Children.Add(fillGrid);
            pGrid.Visibility = shouldHaveProgress ? Visibility.Visible : Visibility.Collapsed;
            usageDetailGrid.Children.Add(pGrid);

            var bg = new Border
            {
                Height = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Background = GetThemeBrush("ProgressBarBackground", Brushes.Gray),
                CornerRadius = new CornerRadius(0),
                Tag = "Part_Background",
                Visibility = shouldHaveProgress ? Visibility.Collapsed : Visibility.Visible
            };
            usageDetailGrid.Children.Add(bg);

            // Details Text (The tokens/credits/cost)
            var detailText = _preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(usage.Description, usage.AccountName) : usage.Description;

            // Tailor description based on PaymentType
            if (usage.PaymentType == PaymentType.Credits)
            {
                var remaining = usage.CostLimit - usage.CostUsed;
                detailText = $"{remaining:F2} {usage.UsageUnit} Remaining";
            }
            else if (usage.PaymentType == PaymentType.UsageBased && usage.CostLimit > 0)
            {
                detailText = $"Spent: ${usage.CostUsed:F2} / Limit: ${usage.CostLimit:F2}";
            }

            string? resetTextFromDetail = null;
            DateTime? detailResetTime = usage.NextResetTime;
            if (!string.IsNullOrEmpty(detailText))
            {
                var rIdx = detailText.IndexOf("(Resets:");
                if (rIdx >= 0)
                {
                    resetTextFromDetail = detailText.Substring(rIdx);
                    detailText = detailText.Substring(0, rIdx).Trim();
                }

                var detailBlock = new TextBlock
                {
                    Text = detailText,
                    FontSize = 10.5,
                    Foreground = GetThemeBrush("SecondaryText", Brushes.Gray),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 200, // Ensure some bar space remains
                    ToolTip = _preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(detailText, usage.AccountName) : detailText,
                    Tag = "Part_StatusText"
                };
                Grid.SetColumn(detailBlock, 1);
                usageDetailGrid.Children.Add(detailBlock);
            }

            if (!string.IsNullOrEmpty(resetTextFromDetail))
            {
                // Add Reset time as a separate line in standard view for maximum visibility
                 grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                 var resetBlock = new TextBlock 
                 { 
                     Text = FormatResetDisplay(resetTextFromDetail, detailResetTime), 
                     FontSize = 10, 
                     Foreground = GetThemeBrush("StatusTextWarning", Brushes.Gray), 
                     Margin = new Thickness(22, 2, 0, 0),
                     FontWeight = FontWeights.SemiBold,
                     Tag = "Part_ResetText"
                 };
                 Grid.SetRow(resetBlock, grid.RowDefinitions.Count - 1);
                 grid.Children.Add(resetBlock);
            }

            grid.Children.Add(usageDetailGrid);
            Grid.SetRow(usageDetailGrid, 1);

            container.MouseDown += (s, e) => {
                _resetDisplayMode = (_resetDisplayMode + 1) % 3;
                Dispatcher.BeginInvoke(new Action(() => RenderUsages(_cachedUsages)));
            };

            // Add detailed tooltip with rate limit information if available
            if (usage.Details != null && usage.Details.Any())
            {
                var tooltipBuilder = new System.Text.StringBuilder();
                tooltipBuilder.AppendLine($"{usage.ProviderName}");
                tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
                if (!string.IsNullOrEmpty(usage.Description))
                {
                    tooltipBuilder.AppendLine($"Description: {usage.Description}");
                }
                tooltipBuilder.AppendLine();
                tooltipBuilder.AppendLine("Rate Limits:");
                foreach (var detail in usage.Details)
                {
                    tooltipBuilder.AppendLine($"  {detail.Name}: {detail.Used}");
                }
                
                // Add warning indicator
                if (usage.UsagePercentage >= 90)
                {
                    tooltipBuilder.AppendLine();
                    tooltipBuilder.AppendLine("⚠️ CRITICAL: Approaching rate limit!");
                }
                else if (usage.UsagePercentage >= 70)
                {
                    tooltipBuilder.AppendLine();
                    tooltipBuilder.AppendLine("⚠️ WARNING: High usage");
                }
                
                container.ToolTip = tooltipBuilder.ToString().Trim();
            }
            else if (!string.IsNullOrEmpty(usage.AccountName) && usage.AccountName.StartsWith("⚠️"))
            {
                // Handle warning message in AccountName
                container.ToolTip = usage.AccountName;
            }

            container.Child = grid;
            return container;
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshData(forceRefresh: true);
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).ShowSettings();
        }

        private ImageSource GetIconForProvider(string providerId)
        {
            if (_iconCache.TryGetValue(providerId, out var cached)) return cached;

            string filename = providerId.ToLower() switch
            {
                "github-copilot" => "github",
                "gemini-cli" => "google",
                "antigravity" => "google",
                "claude-code" => "claude",
                "minimax" => "minimax",
                "minimax-io" => "minimax",
                "minimax-global" => "minimax",
                "kimi" => "kimi",
                "xiaomi" => "xiaomi",
                _ => providerId.ToLower()
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
                        _iconCache[providerId] = image;
                        return image;
                    }
                }
                catch
                {
                }
            }

            var icoPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.ico");
            if (System.IO.File.Exists(icoPath))
            {
                try
                {
                    var icoImage = new System.Windows.Media.Imaging.BitmapImage();
                    icoImage.BeginInit();
                    icoImage.UriSource = new Uri(icoPath);
                    icoImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    icoImage.EndInit();
                    icoImage.Freeze();
                    _iconCache[providerId] = icoImage;
                    return icoImage;
                }
                catch
                {
                }
            }

            var fallback = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/AIConsumptionTracker.UI;component/Assets/usage_icon.png"));
            fallback.Freeze();
            _iconCache[providerId] = fallback;
            return fallback;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async Task CheckForUpdates()
        {
            _latestUpdate = await _updateChecker.CheckForUpdatesAsync();
            if (_latestUpdate != null)
            {
                if (UpdateNotificationBanner != null)
                {
                    UpdateText.Text = $"New version available: {_latestUpdate.Version}";
                    UpdateNotificationBanner.Visibility = Visibility.Visible;
                }
            }
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_latestUpdate != null)
            {
                try
                {
                    // Show confirmation dialog
                    var result = MessageBox.Show(
                        $"Download and install version {_latestUpdate.Version}?\n\nThe application will restart after installation.",
                        "Confirm Update",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Show progress dialog
                        var progressWindow = new Window
                        {
                            Title = "Downloading Update",
                            Width = 400,
                            Height = 150,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            ResizeMode = ResizeMode.NoResize,
                            Content = new StackPanel
                            {
                                Margin = new Thickness(20),
                                Children =
                                {
                                    new TextBlock { Text = $"Downloading version {_latestUpdate.Version}...", Margin = new Thickness(0, 0, 0, 10) },
                                    new ProgressBar { Name = "ProgressBar", Height = 20, Minimum = 0, Maximum = 100 }
                                }
                            }
                        };

                        var progressBar = (ProgressBar)((StackPanel)progressWindow.Content).Children[1];
                        var progress = new Progress<double>(p => progressBar.Value = p);

                        progressWindow.Show();

                        // Download and install
                        var success = await _updateChecker.DownloadAndInstallUpdateAsync(_latestUpdate, progress);

                        progressWindow.Close();

                        if (success)
                        {
                            // Close the application - installer will restart it
                            Application.Current.Shutdown();
                        }
                        else
                        {
                            MessageBox.Show("Failed to download or install the update. Please try again or download manually from the releases page.", "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Update error: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ViewChangelogBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_latestUpdate != null)
            {
                // Create changelog window
                var changelogWindow = new Window
                {
                    Title = $"Changelog - Version {_latestUpdate.Version}",
                    Width = 600,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = GetThemeBrush("CardBackground", Brushes.Gray),
                    Foreground = GetThemeBrush("PrimaryText", Brushes.Gray),
                    ResizeMode = ResizeMode.CanResize,
                    MinWidth = 400,
                    MinHeight = 300
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(20)
                };

                var textBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    LineHeight = 20
                };

                if (!string.IsNullOrEmpty(_latestUpdate.ReleaseNotes))
                {
                    textBlock.Text = _latestUpdate.ReleaseNotes;
                }
                else
                {
                    textBlock.Text = "No changelog available for this version.";
                    textBlock.FontStyle = FontStyles.Italic;
                    textBlock.Foreground = Brushes.Gray;
                }

                scrollViewer.Content = textBlock;
                changelogWindow.Content = scrollViewer;
                changelogWindow.ShowDialog();
            }
        }

        public Brush GetProgressBarColor(double percentage, bool isQuota)
        {
            // ===================================================================
            // ⚠️  AI ASSISTANTS: DO NOT MODIFY THIS LOGIC WITHOUT DEVELOPER APPROVAL
            // ===================================================================
            // This color logic follows strict design rules documented in DESIGN.md
            // Quota providers: Inverted thresholds (100 - userThreshold)
            // Usage providers: Standard thresholds (userThreshold)
            // See DESIGN.md section "Color Determination" for full specification
            // When in doubt, ASK THE DEVELOPER FIRST before changing anything
            // ===================================================================
            
            // For quota-based providers: percentage represents REMAINING
            // - High remaining (> YellowThreshold) = GREEN (healthy)
            // - Mid remaining (> RedThreshold) = YELLOW (warning)
            // - Low remaining (< RedThreshold) = RED (critical)
            // For usage-based providers: percentage represents USED
            // - Low usage (< YellowThreshold) = GREEN (healthy)
            // - Mid usage (> YellowThreshold) = YELLOW (warning)
            // - High usage (> RedThreshold) = RED (critical)
            
            if (isQuota)
            {
                // Inverted thresholds for quota (remaining %)
                if (percentage < (100 - _preferences.ColorThresholdRed))
                    return Brushes.Crimson;
                else if (percentage < (100 - _preferences.ColorThresholdYellow))
                    return Brushes.Gold;
                else
                    return Brushes.MediumSeaGreen;
            }
            else
            {
                // Standard thresholds for usage-based (used %)
                if (percentage > _preferences.ColorThresholdRed)
                    return Brushes.Crimson;
                else if (percentage > _preferences.ColorThresholdYellow)
                    return Brushes.Gold;
                else
                    return Brushes.MediumSeaGreen;
            }
        }

        private string GetUsageText(ProviderUsage usage)
        {
            bool isMissing = usage.Description.Contains("not found", StringComparison.OrdinalIgnoreCase);
            bool isError = usage.Description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);

            if (isMissing) return "Key Missing";
            if (isError) return "Error";

            if (usage.PaymentType == PaymentType.Credits)
            {
                var remaining = usage.CostLimit - usage.CostUsed;
                return $"{remaining:F2} Rem";
            }
            else if (usage.PaymentType == PaymentType.UsageBased && usage.CostLimit > 0)
            {
                return $"${usage.CostUsed:F2} / ${usage.CostLimit:F2}";
            }

            return _preferences.IsPrivacyMode ? PrivacyHelper.MaskContent(usage.Description, usage.AccountName) : usage.Description;
        }

        private UIElement CreateProviderBar(ProviderUsage usage, bool isChild = false)
        {
            var element = _preferences.CompactMode ? CreateCompactItem(usage, isChild) : CreateStandardItem(usage, isChild);
            if (element is FrameworkElement fe) fe.Tag = usage.ProviderId;
            return element;
        }
    }
}
