using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.AgentClient;
using SharpVectors.Renderers.Wpf;
using SharpVectors.Converters;

namespace AIConsumptionTracker.UI.Slim;

public enum StatusType
{
    Info,
    Success,
    Warning,
    Error
}

public partial class MainWindow : Window
{
    private readonly AgentService _agentService;
    private AppPreferences _preferences = new();
    private List<ProviderUsage> _usages = new();
    private bool _isPrivacyMode = false;
    private bool _isLoading = false;
    private readonly Dictionary<string, ImageSource> _iconCache = new();
    private DateTime _lastAgentUpdate = DateTime.MinValue;
    private DispatcherTimer? _pollingTimer;

    public MainWindow()
    {
        InitializeComponent();
        _agentService = new AgentService();

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
        };

        // Track window position changes
        LocationChanged += async (s, e) => await SaveWindowPositionAsync();
        SizeChanged += async (s, e) => await SaveWindowPositionAsync();
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
        if (!IsLoaded || _agentService == null) return;

        // Only save if position has changed meaningfully
        if (Math.Abs(_preferences.WindowLeft.GetValueOrDefault() - Left) > 1 ||
            Math.Abs(_preferences.WindowTop.GetValueOrDefault() - Top) > 1)
        {
            _preferences.WindowLeft = Left;
            _preferences.WindowTop = Top;
            await _agentService.SavePreferencesAsync(_preferences);
        }
    }

    private async Task InitializeAsync()
    {
        if (_isLoading || _agentService == null)
            return;

        try
        {
            _isLoading = true;
            ShowStatus("Checking agent status...", StatusType.Info);

            // Offload the expensive discovery/startup logic to a background thread
            // to prevent UI freezing during port scans or agent startup waits.
            var success = await Task.Run(async () => {
                try {
                    // Refresh port discovery
                    await _agentService.RefreshPortAsync();

                    // Check if Agent is running, auto-start if needed
                    if (!await AgentLauncher.IsAgentRunningAsync())
                    {
                        Dispatcher.Invoke(() => ShowStatus("Agent not running. Starting agent...", StatusType.Warning));

                        if (await AgentLauncher.StartAgentAsync())
                        {
                            Dispatcher.Invoke(() => ShowStatus("Waiting for agent...", StatusType.Warning));
                            var agentReady = await AgentLauncher.WaitForAgentAsync();

                            if (!agentReady)
                            {
                                Dispatcher.Invoke(() => {
                                    ShowStatus("Agent failed to start", StatusType.Error);
                                    ShowErrorState("Agent failed to start.\n\nPlease ensure AIConsumptionTracker.Agent is installed and try again.");
                                });
                                return false;
                            }

                        }
                        else
                        {
                            Dispatcher.Invoke(() => {
                                ShowStatus("Could not start agent", StatusType.Error);
                                ShowErrorState("Could not start agent automatically.\n\nPlease start it manually:\n\ndotnet run --project AIConsumptionTracker.Agent");
                            });
                            return false;
                        }
                    }

                    // Load preferences on background thread
                    var prefs = await _agentService.GetPreferencesAsync();
                    Dispatcher.Invoke(() => {
                        _preferences = prefs;
                        _isPrivacyMode = _preferences.IsPrivacyMode;
                        ApplyPreferences();
                    });

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

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                // Try to get cached data from agent
                var usages = await _agentService.GetUsageAsync();

                if (usages.Any())
                {
                    // Data is available - render and stop rapid polling
                    _usages = usages.ToList();
                    RenderProviders();
                    _lastAgentUpdate = DateTime.Now;
                    ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                    return;
                }

                // No data yet - wait and try again
                if (attempt < maxAttempts - 1)
                {
                    ShowStatus($"Waiting for data... ({attempt + 1}/{maxAttempts})", StatusType.Warning);
                    await Task.Delay(pollIntervalMs);
                }
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
        ShowErrorState("No provider data available.\n\nThe Agent may still be initializing.\nTry refreshing manually.");
    }

    private void ApplyPreferences()
    {
        // Apply window settings
        this.Topmost = _preferences.AlwaysOnTop;
        this.Width = _preferences.WindowWidth;
        this.Height = _preferences.WindowHeight;

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

        // Filter out Antigravity completely if not available
        // Also filter out Antigravity child items (antigravity.*) as they are shown inside the main card
        filteredUsages = filteredUsages.Where(u =>
            !(u.ProviderId == "antigravity" && !u.IsAvailable) &&
            !u.ProviderId.StartsWith("antigravity.")
        ).ToList();

        // Separate providers by type and order alphabetically
        var quotaProviders = filteredUsages.Where(u => u.IsQuotaBased || u.PlanType == PlanType.Coding).OrderBy(u => u.ProviderName).ToList();
        var paygProviders = filteredUsages.Where(u => !u.IsQuotaBased && u.PlanType != PlanType.Coding).OrderBy(u => u.ProviderName).ToList();

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
                // Add all quota providers with Antigravity model rows grouped by Antigravity groups
                foreach (var usage in quotaProviders.OrderBy(u => u.ProviderName))
                {
                    AddProviderCard(usage, plansContainer);

                    if (usage.ProviderId.Equals("antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        AddGroupedAntigravityModels(usage, plansContainer);
                    }
                    else if (usage.Details?.Any() == true)
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
                foreach (var usage in paygProviders.OrderBy(u => u.ProviderName))
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
            if (_agentService != null)
                await _agentService.SavePreferencesAsync(_preferences);
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
        // Compact horizontal bar similar to non-slim UI
        bool isMissing = usage.Description.Contains("not found", StringComparison.OrdinalIgnoreCase);
        bool isConsoleCheck = usage.Description.Contains("Check Console", StringComparison.OrdinalIgnoreCase);
        bool isError = usage.Description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);
        bool isAntigravityParent = usage.ProviderId.Equals("antigravity", StringComparison.OrdinalIgnoreCase);

        // Main Grid Container - single row layout
        var grid = new Grid
        {
            Margin = new Thickness(isChild ? 20 : 0, 0, 0, 2),
            Height = 24,
            Background = Brushes.Transparent,
            Tag = usage.ProviderId
        };

        bool shouldHaveProgress = !isAntigravityParent && (usage.RequestsPercentage > 0 || usage.IsQuotaBased) && !isMissing && !isError;

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
        double indicatorWidth = showUsed ? pctUsed : pctRemaining;

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

            if (isAntigravityParent)
            {
                statusText = string.IsNullOrWhiteSpace(usage.Description)
                    ? "Per-model quotas"
                    : usage.Description;
            }
            else if (usage.PlanType == PlanType.Coding)
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
            else if (usage.PlanType == PlanType.Usage && usage.RequestsAvailable > 0)
            {
                var showUsedPercent = ShowUsedToggle?.IsChecked ?? false;
                var usedPercent = UsageMath.ClampPercent(usage.RequestsPercentage);
                statusText = showUsedPercent
                    ? $"{usedPercent:F0}% used"
                    : $"{(100.0 - usedPercent):F0}% remaining";
            }
            else if (usage.IsQuotaBased || usage.PlanType == PlanType.Coding)
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
            Text = $"{usage.ProviderName}{accountPart}",
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
                tooltipBuilder.AppendLine($"  {GetDetailDisplayName(detail)}: {detail.Used}");
            }
            grid.ToolTip = tooltipBuilder.ToString().Trim();
        }
        else if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            grid.ToolTip = $"{usage.ProviderName}\nSource: {usage.AuthSource}";
        }

        container.Children.Add(grid);
    }

    private void AddGroupedAntigravityModels(ProviderUsage usage, StackPanel container)
    {
        if (usage.Details?.Any() != true)
        {
            return;
        }

        var groupedDetails = usage.Details
            .Where(d => !string.IsNullOrWhiteSpace(GetAntigravityModelDisplayName(d)) && !d.Name.StartsWith("[", StringComparison.Ordinal))
            .GroupBy(ResolveAntigravityGroupHeader)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedDetails)
        {
            var groupHeader = CreateText(
                group.Key!,
                9.0,
                GetResourceBrush("SecondaryText", Brushes.Gray),
                FontWeights.SemiBold,
                new Thickness(20, 4, 0, 2));
            container.Children.Add(groupHeader);

            foreach (var detail in group.OrderBy(GetAntigravityModelDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                AddProviderCard(CreateAntigravityModelUsage(detail, usage), container, isChild: true);
            }
        }
    }

    private static ProviderUsage CreateAntigravityModelUsage(ProviderUsageDetail detail, ProviderUsage parentUsage)
    {
        var remainingPercent = ParsePercent(detail.Used);
        return new ProviderUsage
        {
            ProviderId = $"antigravity.{detail.Name.ToLowerInvariant().Replace(" ", "-")}",
            ProviderName = GetAntigravityModelDisplayName(detail),
            RequestsPercentage = remainingPercent,
            RequestsUsed = 100.0 - remainingPercent,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = $"{remainingPercent:F0}% Remaining",
            NextResetTime = detail.NextResetTime,
            IsAvailable = parentUsage.IsAvailable,
            AuthSource = parentUsage.AuthSource
        };
    }

    private static double ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var parsedValue = value.Replace("%", "").Trim();
        return double.TryParse(parsedValue, out var parsed)
            ? Math.Max(0, Math.Min(100, parsed))
            : 0;
    }

    private static string GetAntigravityModelDisplayName(ProviderUsageDetail detail)
    {
        return string.IsNullOrWhiteSpace(detail.ModelName) ? detail.Name : detail.ModelName;
    }

    private static string GetDetailDisplayName(ProviderUsageDetail detail)
    {
        return string.IsNullOrWhiteSpace(detail.ModelName) ? detail.Name : detail.ModelName;
    }

    private static string ResolveAntigravityGroupHeader(ProviderUsageDetail detail)
    {
        return string.IsNullOrWhiteSpace(detail.GroupName) ? "Ungrouped Models" : detail.GroupName;
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

        // Try parse percentage
        var valueText = detail.Used?.Replace("%", "").Trim();
        if (double.TryParse(valueText, out double val))
        {
            pctRemaining = val; // Antigravity sends Remaining % in this field
            pctUsed = Math.Max(0, 100 - pctRemaining);
        }

        // Determine display values based on toggle
        bool showUsed = ShowUsedToggle?.IsChecked ?? false;
        double displayPct = showUsed ? pctUsed : pctRemaining;
        string displayStr = $"{displayPct:F0}%";

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
        grid.Children.Add(pGrid);

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
            foreach (var detail in usage.Details)
            {
                AddSubProviderCard(detail, subContainer);
            }
        }
    }

    private string GetRelativeTimeString(DateTime nextReset)
    {
        var diff = nextReset - DateTime.Now;

        if (diff.TotalSeconds <= 0) return "Ready";
        if (diff.TotalDays >= 1) return $"{diff.Days}d {diff.Hours}h";
        if (diff.TotalHours >= 1) return $"{diff.Hours}h {diff.Minutes}m";
        return $"{diff.Minutes}m";
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
        // Check cache first
        if (_iconCache.TryGetValue(providerId, out var cachedImage))
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
        string filename = providerId.ToLower() switch
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
        return CreateFallbackIcon(providerId);
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
                if (usages.Any())
                {
                    _usages = usages.ToList();
                    RenderProviders();
                    _lastAgentUpdate = DateTime.Now;
                    ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Polling error: {ex.Message}");
            }
        };

        _pollingTimer.Start();
    }

    private void ShowStatus(string message, StatusType type)
    {
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
            StatusLed.ToolTip = tooltipText;
        if (StatusText != null)
            StatusText.ToolTip = tooltipText;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Debug.WriteLine($"[{timestamp}] [{type}] {message}");
        Console.WriteLine($"[{timestamp}] [{type}] {message}");
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

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();

        // Handle non-modal window closed event
        settingsWindow.Closed += async (s, args) =>
        {
            if (settingsWindow.SettingsChanged)
            {
                // Reload preferences and refresh data
                await InitializeAsync();
            }
        };

        settingsWindow.Show();
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
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIConsumptionTracker.Web", "bin", "Debug", "net8.0", "AIConsumptionTracker.Web.exe"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIConsumptionTracker.Web", "bin", "Release", "net8.0", "AIConsumptionTracker.Web.exe"),
                Path.Combine(AppContext.BaseDirectory, "AIConsumptionTracker.Web.exe"),
            };

            var webPath = possiblePaths.FirstOrDefault(File.Exists);

            if (webPath == null)
            {
                // Try dotnet run
                var webProjectDir = FindProjectDirectory("AIConsumptionTracker.Web");
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
        _isPrivacyMode = !_isPrivacyMode;
        _preferences.IsPrivacyMode = _isPrivacyMode;
        if (_agentService != null)
            await _agentService.SavePreferencesAsync(_preferences);
        RenderProviders();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private async void AlwaysOnTop_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _preferences.AlwaysOnTop = AlwaysOnTopCheck.IsChecked ?? true;
        this.Topmost = _preferences.AlwaysOnTop;
        if (_agentService != null)
            await _agentService.SavePreferencesAsync(_preferences);
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
        if (_agentService != null)
            await _agentService.SavePreferencesAsync(_preferences);

        // Refresh the display to show used% vs remaining%
        RenderProviders();
    }

    private void RefreshData_NoArgs(object sender, RoutedEventArgs e)
    {
        _ = RefreshDataAsync();
    }

    private void ViewChangelogBtn_Click(object sender, RoutedEventArgs e)
    {
        // Open changelog in browser or show dialog
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/rygel/AIConsumptionTracker/releases",
            UseShellExecute = true
        });
    }

    private void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        // Trigger update download
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/rygel/AIConsumptionTracker/releases/latest",
            UseShellExecute = true
        });
    }


    private async Task RestartAgentAsync()
    {
        try
        {
            ShowStatus("Restarting agent...", StatusType.Warning);

            // Try to start agent
            if (await AgentLauncher.StartAgentAsync())
            {
                var agentReady = await AgentLauncher.WaitForAgentAsync();
                if (agentReady)
                {
                    ShowStatus("Agent restarted", StatusType.Success);
                    await RefreshDataAsync();
                }
                else
                {
                    ShowStatus("Agent restart failed", StatusType.Error);
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
        var (isRunning, _) = await AgentLauncher.IsAgentRunningWithPortAsync();

        if (isRunning)
        {
            // Stop the agent
            ShowStatus("Stopping agent...", StatusType.Warning);
            var stopped = await AgentLauncher.StopAgentAsync();
            if (stopped)
            {
                ShowStatus("Agent stopped", StatusType.Info);
                UpdateAgentToggleButton(false);
            }
            else
            {
                ShowStatus("Failed to stop agent", StatusType.Error);
            }
        }
        else
        {
            // Start the agent
            ShowStatus("Starting agent...", StatusType.Warning);
            if (await AgentLauncher.StartAgentAsync())
            {
                var agentReady = await AgentLauncher.WaitForAgentAsync();
                if (agentReady)
                {
                    ShowStatus("Agent started", StatusType.Success);
                    UpdateAgentToggleButton(true);
                    await RefreshDataAsync();
                }
                else
                {
                    ShowStatus("Agent failed to start", StatusType.Error);
                    UpdateAgentToggleButton(false);
                }
            }
            else
            {
                ShowStatus("Could not start agent", StatusType.Error);
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
            AgentToggleBtn.ToolTip = isRunning ? "Stop Agent" : "Start Agent";
        }
    }

    private async Task UpdateAgentToggleButtonStateAsync()
    {
        var (isRunning, _) = await AgentLauncher.IsAgentRunningWithPortAsync();
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
