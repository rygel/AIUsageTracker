using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIConsumptionTracker.UI.Slim.Models;
using AIConsumptionTracker.UI.Slim.Services;
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

        Loaded += async (s, e) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_isLoading || _agentService == null)
            return;

        try
        {
            _isLoading = true;
            ShowStatus("Checking agent status...", StatusType.Info);
            
            // Refresh port discovery before making API calls
            await _agentService.RefreshPortAsync();

            // Check if Agent is running, auto-start if needed
            if (!await AgentLauncher.IsAgentRunningAsync())
            {
                ShowStatus("Agent not running. Starting agent...", StatusType.Warning);
                
                if (AgentLauncher.StartAgent())
                {
                    ShowStatus("Waiting for agent...", StatusType.Warning);
                    var agentReady = await AgentLauncher.WaitForAgentAsync();
                    
                    if (!agentReady)
                    {
                        ShowStatus("Agent failed to start", StatusType.Error);
                        ShowErrorState("Agent failed to start.\n\nPlease ensure AIConsumptionTracker.Agent is installed and try again.");
                        return;
                    }
                    
                    ShowStatus("Agent started", StatusType.Success);
                    UpdateAgentStatusButton(true);
                }
                else
                {
                    ShowStatus("Could not start agent", StatusType.Error);
                    ShowErrorState("Could not start agent automatically.\n\nPlease start it manually:\n\ndotnet run --project AIConsumptionTracker.Agent");
                    UpdateAgentStatusButton(false);
                    return;
                }
            }
            else
            {
                UpdateAgentStatusButton(true);
            }

            // Load preferences
            _preferences = await _agentService.GetPreferencesAsync();
            _isPrivacyMode = _preferences.IsPrivacyMode;

            // Apply preferences to UI
            ApplyPreferences();

            // Rapid polling at startup until data is available
            await RapidPollUntilDataAvailableAsync();

            // Start polling timer - UI polls Agent every minute
            StartPollingTimer();

            ShowStatus("Ready", StatusType.Success);
        }
        catch (Exception ex)
        {
            ShowErrorState($"Initialization failed: {ex.Message}");
            UpdateAgentStatusButton(false);
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

        // Apply UI controls
        ShowAllToggle.IsChecked = _preferences.ShowAll;
        AlwaysOnTopCheck.IsChecked = _preferences.AlwaysOnTop;
        CompactCheck.IsChecked = _preferences.CompactMode;
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
            return;
        }

        var filteredUsages = _preferences.ShowAll ? _usages : _usages.Where(u => u.IsAvailable).ToList();

        // Filter out Antigravity completely if not available (regardless of ShowAll setting)
        filteredUsages = filteredUsages.Where(u => !(u.ProviderId == "antigravity" && !u.IsAvailable)).ToList();

        // Separate providers by type and order alphabetically
        var quotaProviders = filteredUsages.Where(u => u.IsQuotaBased || u.PaymentType == PaymentType.Quota).OrderBy(u => u.ProviderName).ToList();
        var paygProviders = filteredUsages.Where(u => !u.IsQuotaBased && u.PaymentType != PaymentType.Quota).OrderBy(u => u.ProviderName).ToList();
        
        // Special handling for Antigravity - check if it has sub-providers
        var antigravityProviders = quotaProviders.Where(u => u.ProviderId == "antigravity").ToList();
        quotaProviders = quotaProviders.Where(u => u.ProviderId != "antigravity").ToList();

        // Plans & Quotas Section
        if (quotaProviders.Any() || antigravityProviders.Any())
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
                // Add regular quota providers
                foreach (var usage in quotaProviders.OrderBy(u => u.ProviderName))
                {
                    AddProviderCard(usage, plansContainer);
                }

                // Special handling for Antigravity with sub-providers
                foreach (var antigravity in antigravityProviders)
                {
                    if (antigravity.Details?.Any() == true)
                    {
                        // Antigravity with collapsible sub-providers
                        var (antiHeader, antiContainer) = CreateCollapsibleSubHeader(
                            antigravity.ProviderName,
                            Brushes.DeepSkyBlue,
                            () => _preferences.IsAntigravityCollapsed,
                            v => _preferences.IsAntigravityCollapsed = v);
                        
                        plansContainer.Children.Add(antiHeader);
                        plansContainer.Children.Add(antiContainer);

                        if (!_preferences.IsAntigravityCollapsed)
                        {
                            // Add main Antigravity card
                            AddProviderCard(antigravity, antiContainer);
                            
                            // Add sub-provider details
                            foreach (var detail in antigravity.Details)
                            {
                                AddSubProviderCard(detail, antiContainer);
                            }
                        }
                    }
                    else
                    {
                        // Regular Antigravity without sub-providers
                        AddProviderCard(antigravity, plansContainer);
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

        // Main Grid Container - single row layout
        var grid = new Grid
        {
            Margin = new Thickness(isChild ? 20 : 0, 0, 0, 2),
            Height = 24,
            Background = Brushes.Transparent,
            Tag = usage.ProviderId
        };

        bool shouldHaveProgress = (usage.UsagePercentage > 0 || usage.IsQuotaBased) && !isMissing && !isError;

        // Background Progress Bar
        var pGrid = new Grid();
        var indicatorWidth = Math.Min(usage.UsagePercentage, 100);
        if (_preferences.InvertProgressBar) indicatorWidth = Math.Max(0, 100 - indicatorWidth);

        pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indicatorWidth, GridUnitType.Star) });
        pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star) });

        var fill = new Border
        {
            Background = GetProgressBarColor(usage.UsagePercentage, usage.IsQuotaBased),
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
            providerIcon.Margin = new Thickness(0, 0, 8, 0);
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
            statusText = _isPrivacyMode ? "***" : usage.Description;
            
            if (usage.PaymentType == PaymentType.Credits)
            {
                var remaining = usage.CostLimit - usage.CostUsed;
                statusText = $"{remaining:F0} remaining";
            }
            else if (usage.PaymentType == PaymentType.UsageBased && usage.CostLimit > 0)
            {
                statusText = $"${usage.CostUsed:F2} / ${usage.CostLimit:F2}";
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
                Margin = new Thickness(6, 0, 0, 0)
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
        var accountPart = string.IsNullOrWhiteSpace(usage.AccountName) ? "" : $" [{(_isPrivacyMode ? MaskProviderName(usage.AccountName) : usage.AccountName)}]";
        var nameBlock = new TextBlock
        {
            Text = _isPrivacyMode 
                ? $"{MaskProviderName(usage.ProviderName)}{accountPart}"
                : $"{usage.ProviderName}{accountPart}",
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
                tooltipBuilder.AppendLine($"  {detail.Name}: {detail.Used}");
            }
            grid.ToolTip = tooltipBuilder.ToString().Trim();
        }
        else if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            grid.ToolTip = $"{usage.ProviderName}\nSource: {usage.AuthSource}";
        }

        container.Children.Add(grid);
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

        // Bullet point
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

        // Value on the right
        var valueBlock = new TextBlock
        {
            Text = detail.Used,
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

    private string GetRelativeTimeString(DateTime nextReset)
    {
        var diff = nextReset - DateTime.Now;
        
        if (diff.TotalSeconds <= 0) return "Ready";
        if (diff.TotalDays >= 1) return $"{diff.Days}d {diff.Hours}h";
        if (diff.TotalHours >= 1) return $"{diff.Hours}h {diff.Minutes}m";
        return $"{diff.Minutes}m";
    }

    private string MaskProviderName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.Length <= 2) return "**";
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

    private Brush GetProgressBarColor(double percentage, bool isQuotaBased)
    {
        // For quota-based providers: high percentage = green (good), low = red (bad)
        // For usage-based providers: low percentage = green (good), high = red (bad)
        
        var yellowThreshold = _preferences.ColorThresholdYellow;
        var redThreshold = _preferences.ColorThresholdRed;
        
        if (isQuotaBased)
        {
            // Quota-based: high remaining % = green, low = red
            if (percentage < redThreshold) return Brushes.Crimson;
            if (percentage < yellowThreshold) return Brushes.Gold;
            return Brushes.MediumSeaGreen;
        }
        else
        {
            // Usage-based: high used % = red, low = green
            if (percentage >= redThreshold) return Brushes.Crimson;
            if (percentage >= yellowThreshold) return Brushes.Gold;
            return Brushes.MediumSeaGreen;
        }
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
        
        Debug.WriteLine($"[{type}] {message}");
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
        Application.Current.Shutdown();
    }

    private async void AlwaysOnTop_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        
        _preferences.AlwaysOnTop = AlwaysOnTopCheck.IsChecked ?? true;
        this.Topmost = _preferences.AlwaysOnTop;
        if (_agentService != null)
            await _agentService.SavePreferencesAsync(_preferences);
    }

    private async void ShowAllToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        
        _preferences.ShowAll = ShowAllToggle.IsChecked ?? true;
        if (_agentService != null)
            await _agentService.SavePreferencesAsync(_preferences);
        RenderProviders();
    }

    private async void Compact_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        
        _preferences.CompactMode = CompactCheck.IsChecked ?? true;
        if (_agentService != null)
            await _agentService.SavePreferencesAsync(_preferences);
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

    private void AgentStatusBtn_Click(object sender, RoutedEventArgs e)
    {
        // Show agent status menu or restart agent
        var result = MessageBox.Show(
            "Agent provides data to this UI.\n\nClick 'Yes' to restart the Agent.",
            "Agent Status",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        
        if (result == MessageBoxResult.Yes)
        {
            _ = RestartAgentAsync();
        }
    }

    private async Task RestartAgentAsync()
    {
        try
        {
            ShowStatus("Restarting agent...", StatusType.Warning);
            UpdateAgentStatusButton(false);
            
            // Try to start agent
            if (AgentLauncher.StartAgent())
            {
                var agentReady = await AgentLauncher.WaitForAgentAsync();
                if (agentReady)
                {
                    ShowStatus("Agent restarted", StatusType.Success);
                    UpdateAgentStatusButton(true);
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

    private void UpdateAgentStatusButton(bool isConnected)
    {
        if (AgentStatusBtn != null)
        {
            AgentStatusBtn.Foreground = isConnected 
                ? GetResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen)
                : GetResourceBrush("ProgressBarRed", Brushes.Crimson);
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
        }
    }
}
