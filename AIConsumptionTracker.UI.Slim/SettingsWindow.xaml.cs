using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.AgentClient;

namespace AIConsumptionTracker.UI.Slim;

public partial class SettingsWindow : Window
{
    private readonly AgentService _agentService;
    private List<ProviderConfig> _configs = new();
    private List<ProviderUsage> _usages = new();
    private AppPreferences _preferences = new();
    private bool _isPrivacyMode = false;

    public bool SettingsChanged { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        _agentService = new AgentService();
        Loaded += SettingsWindow_Loaded;
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _configs = await _agentService.GetConfigsAsync();
        _usages = await _agentService.GetUsageAsync();
        _preferences = await _agentService.GetPreferencesAsync();
        _isPrivacyMode = _preferences.IsPrivacyMode;

        PopulateProviders();
        PopulateLayoutSettings();
        await LoadHistoryAsync();
        await UpdateAgentStatusAsync();
    }

    private async Task UpdateAgentStatusAsync()
    {
        try
        {
            // Check if agent is running
            var isRunning = await AgentLauncher.IsAgentRunningAsync();
            
            // Get the actual port from the agent
            int port = await AgentLauncher.GetAgentPortAsync();
            
            if (AgentStatusText != null)
            {
                AgentStatusText.Text = isRunning ? "Running" : "Not Running";
            }
            
            // Update port display
            if (FindName("AgentPortText") is TextBlock portText)
            {
                portText.Text = port.ToString();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update agent status: {ex.Message}");
            if (AgentStatusText != null)
            {
                AgentStatusText.Text = "Error";
            }
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _agentService.GetHistoryAsync(100);
            HistoryDataGrid.ItemsSource = history;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load history: {ex.Message}");
        }
    }

    private void PopulateProviders()
    {
        ProvidersStack.Children.Clear();

        if (_configs.Count == 0)
        {
            ProvidersStack.Children.Add(new TextBlock
            {
                Text = "No providers configured. Click 'Scan for Keys' to discover API keys.",
                Foreground = FindResource("TertiaryText") as SolidColorBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            });
            return;
        }

        var groupedConfigs = _configs.OrderBy(c => c.ProviderId).ToList();

        foreach (var config in groupedConfigs)
        {
            var usage = _usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
            AddProviderCard(config, usage);
        }
    }

    private void AddProviderCard(ProviderConfig config, ProviderUsage? usage)
    {
        // Compact card with minimal padding
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

        // Header: Icon + Name
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        
        // Small icon (16x16)
        var icon = CreateProviderIcon(config.ProviderId);
        icon.Width = 16;
        icon.Height = 16;
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        headerPanel.Children.Add(icon);

        // Display name
        var displayName = config.ProviderId switch {
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
                config.ProviderId.Replace("_", " ").Replace("-", " "))
        };

        var title = new TextBlock
        {
            Text = displayName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 120
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        headerPanel.Children.Add(title);

        // Tray checkbox
        var trayCheckBox = new CheckBox
        {
            Content = "Tray",
            IsChecked = config.ShowInTray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(12, 0, 0, 0)
        };
        trayCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        trayCheckBox.Checked += (s, e) => { config.ShowInTray = true; };
        trayCheckBox.Unchecked += (s, e) => { config.ShowInTray = false; };
        headerPanel.Children.Add(trayCheckBox);

        // Notification checkbox
        var notifyCheckBox = new CheckBox
        {
            Content = "Notify",
            IsChecked = config.EnableNotifications,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0)
        };
        notifyCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        notifyCheckBox.Checked += (s, e) => { config.EnableNotifications = true; };
        notifyCheckBox.Unchecked += (s, e) => { config.EnableNotifications = false; };
        headerPanel.Children.Add(notifyCheckBox);

        // Status badge if not configured
        bool isInactive = string.IsNullOrEmpty(config.ApiKey);
        if (config.ProviderId == "antigravity")
        {
            isInactive = usage == null || !usage.IsAvailable;
        }

        if (isInactive)
        {
            var status = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(205, 92, 92)), // IndianRed - pastel red
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(8, 3, 8, 3)
            };

            var badgeText = new TextBlock 
            { 
                Text = "Inactive", 
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240)), // Muted white
                FontWeight = FontWeights.SemiBold
            };
            status.Child = badgeText;
            headerPanel.Children.Add(status);
        }

        grid.Children.Add(headerPanel);

        // Input row
        var keyPanel = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (config.ProviderId == "antigravity")
        {
            // Antigravity: Auto-Detection
            var statusPanel = new StackPanel { Orientation = Orientation.Vertical };
            bool isConnected = usage != null && usage.IsAvailable;
            string accountInfo = usage?.AccountName ?? "Unknown";
            var displayAccount = _isPrivacyMode
                ? MaskAccountIdentifier(accountInfo)
                : accountInfo;

            var statusText = new TextBlock
            {
                Text = isConnected ? $"Auto-Detected ({displayAccount})" : "Searching for local process...",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                FontStyle = isConnected ? FontStyles.Normal : FontStyles.Italic
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, 
                isConnected ? "ProgressBarGreen" : "TertiaryText");

            statusPanel.Children.Add(statusText);

            var antigravitySubmodels = usage?.Details?
                .Select(d => d.Name)
                .Where(name =>
                    !string.IsNullOrWhiteSpace(name) &&
                    !name.StartsWith("[", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (antigravitySubmodels is { Count: > 0 })
            {
                var modelsText = new TextBlock
                {
                    Text = $"Models: {string.Join(", ", antigravitySubmodels)}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                modelsText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                statusPanel.Children.Add(modelsText);
            }

            Grid.SetColumn(statusPanel, 0);
            keyPanel.Children.Add(statusPanel);
        }
        else if (config.ProviderId == "github-copilot")
        {
            // GitHub Copilot: Show username (if available) - privacy mode only shows masked username
            var statusPanel = new StackPanel { Orientation = Orientation.Horizontal };
            string? username = usage?.AccountName;
            bool hasUsername = !string.IsNullOrEmpty(username) && username != "Unknown";

            bool isAuthenticated = !string.IsNullOrEmpty(config.ApiKey);

            string displayText;
            if (!isAuthenticated)
            {
                displayText = "Not Authenticated";
            }
            else if (!hasUsername)
            {
                displayText = "Authenticated (click Refresh to load username)";
            }
            else if (_isPrivacyMode && username != null)
            {
                displayText = $"Authenticated ({MaskAccountIdentifier(username)})";
            }
            else
            {
                // Normal mode: show full text with username
                displayText = $"Authenticated ({username})";
            }

            var statusText = new TextBlock
            {
                Text = displayText,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, 
                isAuthenticated ? "ProgressBarGreen" : "TertiaryText");

            statusPanel.Children.Add(statusText);
            Grid.SetColumn(statusPanel, 0);
            keyPanel.Children.Add(statusPanel);
        }
        else
        {
            // Standard API Key Input
            var displayKey = config.ApiKey;
            if (_isPrivacyMode && !string.IsNullOrEmpty(displayKey))
            {
                if (displayKey.Length > 8)
                    displayKey = displayKey.Substring(0, 4) + "****" + displayKey.Substring(displayKey.Length - 4);
                else
                    displayKey = "****";
            }

            var keyBox = new TextBox
            {
                Text = displayKey,
                Tag = config,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11,
                IsReadOnly = _isPrivacyMode
            };
            
            if (!_isPrivacyMode)
            {
                keyBox.TextChanged += (s, e) => {
                    config.ApiKey = keyBox.Text;
                    SettingsChanged = true;
                };
            }

            Grid.SetColumn(keyBox, 0);
            keyPanel.Children.Add(keyBox);
        }

        Grid.SetRow(keyPanel, 1);
        grid.Children.Add(keyPanel);

        card.Child = grid;
        ProvidersStack.Children.Add(card);
    }

    private FrameworkElement CreateProviderIcon(string providerId)
    {
        // Map to SVG or create fallback
        var image = new Image();
        image.Source = GetProviderImageSource(providerId);
        return image;
    }

    private ImageSource GetProviderImageSource(string providerId)
    {
        try
        {
            string filename = providerId.ToLower() switch
            {
                "github-copilot" => "github",
                "gemini-cli" => "google",
                "antigravity" => "google",
                "claude-code" => "claude",
                "zai" => "zai",
                "zai-coding-plan" => "zai",
                "minimax" => "minimax",
                "minimax-io" => "minimax",
                "minimax-global" => "minimax",
                "kimi" => "kimi",
                "xiaomi" => "xiaomi",
                _ => providerId.ToLower()
            };

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
        catch { }

        return CreateFallbackIcon(providerId);
    }

    private ImageSource CreateFallbackIcon(string providerId)
    {
        // Create a simple colored circle as fallback
        var (color, _) = providerId.ToLower() switch
        {
            "openai" => (Brushes.DarkCyan, "AI"),
            "anthropic" => (Brushes.IndianRed, "An"),
            "github-copilot" => (Brushes.MediumPurple, "GH"),
            "gemini" or "google" => (Brushes.DodgerBlue, "G"),
            "deepseek" => (Brushes.DeepSkyBlue, "DS"),
            _ => (Brushes.Gray, "?")
        };

        // Return a drawing image with just a colored rectangle (simplified)
        var drawing = new GeometryDrawing(
            color,
            new Pen(Brushes.Transparent, 0),
            new RectangleGeometry(new Rect(0, 0, 16, 16)));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private string MaskString(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (input.Length <= 2)
        {
            return new string('*', input.Length);
        }

        return input[0] + new string('*', input.Length - 2) + input[^1];
    }

    private string MaskAccountIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var atIndex = input.IndexOf('@');
        if (atIndex > 0 && atIndex < input.Length - 1)
        {
            var localPart = input[..atIndex];
            var domainPart = input[(atIndex + 1)..];
            var maskedDomainChars = domainPart.ToCharArray();
            for (var i = 0; i < maskedDomainChars.Length; i++)
            {
                if (maskedDomainChars[i] != '.')
                {
                    maskedDomainChars[i] = '*';
                }
            }

            var maskedDomain = new string(maskedDomainChars);
            if (localPart.Length <= 2)
            {
                return $"{new string('*', localPart.Length)}@{maskedDomain}";
            }

            return $"{localPart[0]}{new string('*', localPart.Length - 2)}{localPart[^1]}@{maskedDomain}";
        }

        return MaskString(input);
    }

    private void PopulateLayoutSettings()
    {
        AlwaysOnTopCheck.IsChecked = _preferences.AlwaysOnTop;
        InvertProgressCheck.IsChecked = _preferences.InvertProgressBar;
        InvertCalculationsCheck.IsChecked = _preferences.InvertCalculations;
        YellowThreshold.Text = _preferences.ColorThresholdYellow.ToString();
        RedThreshold.Text = _preferences.ColorThresholdRed.ToString();
        
        // Font settings
        PopulateFontComboBox();
        FontFamilyCombo.SelectedItem = _preferences.FontFamily;
        FontSizeBox.Text = _preferences.FontSize.ToString();
        FontBoldCheck.IsChecked = _preferences.FontBold;
        FontItalicCheck.IsChecked = _preferences.FontItalic;
        UpdateFontPreview();
    }

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

    private void ResetFontBtn_Click(object sender, RoutedEventArgs e)
    {
        // Reset to defaults
        _preferences.FontFamily = "Segoe UI";
        _preferences.FontSize = 12;
        _preferences.FontBold = false;
        _preferences.FontItalic = false;
        
        // Update UI
        FontFamilyCombo.SelectedItem = _preferences.FontFamily;
        FontSizeBox.Text = _preferences.FontSize.ToString();
        FontBoldCheck.IsChecked = _preferences.FontBold;
        FontItalicCheck.IsChecked = _preferences.FontItalic;
        UpdateFontPreview();
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontFamilyCombo.SelectedItem is string font)
        {
            _preferences.FontFamily = font;
            UpdateFontPreview();
        }
    }

    private void FontSizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(FontSizeBox.Text, out int size) && size > 0 && size <= 72)
        {
            _preferences.FontSize = size;
            UpdateFontPreview();
        }
    }

    private void FontBoldCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        _preferences.FontBold = FontBoldCheck.IsChecked ?? false;
        UpdateFontPreview();
    }

    private void FontItalicCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        _preferences.FontItalic = FontItalicCheck.IsChecked ?? false;
        UpdateFontPreview();
    }

    private void PrivacyBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPrivacyMode = !_isPrivacyMode;
        PopulateProviders();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("API key scanning is not yet implemented in AI Consumption Tracker.", 
            "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Trigger refresh on agent
            await _agentService.TriggerRefreshAsync();
            
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

    private async void RefreshHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = await _agentService.GetHistoryAsync(100);
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

    private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        HistoryDataGrid.ItemsSource = null;
    }

    private async void RestartAgentBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Kill any running agent process
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("AIConsumptionTracker.Agent"))
            {
                try { process.Kill(); } catch { }
            }
            
            await Task.Delay(1000);
            
            // Restart agent
            if (await AgentLauncher.StartAgentAsync())
            {
                MessageBox.Show("Agent restarted successfully.", "Restart Complete", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to restart Agent.", "Restart Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to restart Agent: {ex.Message}", "Restart Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckHealthBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, port) = await AgentLauncher.IsAgentRunningWithPortAsync();
            var status = isRunning ? "Running" : "Not Running";
            
            MessageBox.Show($"Agent Status: {status}\n\nPort: {port}", "Health Check", 
                MessageBoxButton.OK, isRunning ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to check health: {ex.Message}", "Health Check Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _preferences.AlwaysOnTop = AlwaysOnTopCheck.IsChecked ?? true;
        _preferences.InvertProgressBar = InvertProgressCheck.IsChecked ?? false;
        _preferences.InvertCalculations = InvertCalculationsCheck.IsChecked ?? false;
        
        if (int.TryParse(YellowThreshold.Text, out int yellow))
            _preferences.ColorThresholdYellow = yellow;
        if (int.TryParse(RedThreshold.Text, out int red))
            _preferences.ColorThresholdRed = red;
        
        // Ensure font settings are saved
        if (FontFamilyCombo.SelectedItem is string font)
            _preferences.FontFamily = font;
        if (int.TryParse(FontSizeBox.Text, out int size) && size > 0 && size <= 72)
            _preferences.FontSize = size;
        _preferences.FontBold = FontBoldCheck.IsChecked ?? false;
        _preferences.FontItalic = FontItalicCheck.IsChecked ?? false;
        
        await _agentService.SavePreferencesAsync(_preferences);
        
        SettingsChanged = true;
        this.Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }
}
