using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Infrastructure.Helpers;

namespace AIConsumptionTracker.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly IConfigLoader _configLoader;
        private readonly ProviderManager _providerManager;
        private readonly IFontProvider _fontProvider;
        private readonly IUpdateCheckerService _updateChecker;
        private readonly IGitHubAuthService _githubAuthService;
        private List<ProviderConfig> _configs = new();
        private AppPreferences _prefs = new();

        public bool SettingsChanged { get; private set; }
        private bool _isScreenshotMode = false;
        private string? _githubUsername;

        public SettingsWindow(IConfigLoader configLoader, ProviderManager providerManager, IFontProvider fontProvider, IUpdateCheckerService updateChecker, IGitHubAuthService githubAuthService)
        {
            InitializeComponent();
            _configLoader = configLoader;
            _providerManager = providerManager;
            _fontProvider = fontProvider;
            _updateChecker = updateChecker;
            _githubAuthService = githubAuthService;
            Loaded += SettingsWindow_Loaded;
        }

        public async Task PrepareForScreenshot(AppPreferences prefs)
        {
            _isScreenshotMode = true;
            _prefs = prefs;
            _configs = await _configLoader.LoadConfigAsync();
            
            // Explicitly select the first tab (Providers)
            if (this.Content is FrameworkElement root)
            {
                var tabControl = root.FindName("MainTabControl") as TabControl;
                if (tabControl != null) tabControl.SelectedIndex = 0;
            }

            await InitializeGitHubAuthAsync();
            PopulateList();
            PopulateLayout();
            UpdateLayout();
            await Task.Yield();
        }

        private async Task InitializeGitHubAuthAsync()
        {
            var copilotConfig = _configs.FirstOrDefault(c => c.ProviderId == "github-copilot");
            if (copilotConfig != null && !string.IsNullOrEmpty(copilotConfig.ApiKey))
            {
                _githubAuthService.InitializeToken(copilotConfig.ApiKey);
                _githubUsername = await _githubAuthService.GetUsernameAsync();
            }
        }

        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isScreenshotMode) return; // Skip if we already prepared for screenshot

            _configs = await _configLoader.LoadConfigAsync();
            _prefs = await _configLoader.LoadPreferencesAsync();

            // Listen for global privacy changes
            if (Application.Current is App app)
            {
                app.PrivacyChanged += (s, isPrivate) => {
                    _prefs.IsPrivacyMode = isPrivate;
                    UpdatePrivacyButton();
                    PopulateList(); // Re-render to update masking
                };
            }

            UpdatePrivacyButton();

            await InitializeGitHubAuthAsync();

            PopulateList();
            PopulateLayout();
        }


        private void PopulateList()
        {
            ProvidersStack.Children.Clear();

            // Get current usage to show dynamic bars
            var usages = _providerManager.LastUsages;

            var groupedConfigs = _configs.OrderBy(c => c.ProviderId).ToList();
            try { System.IO.File.WriteAllText(@"c:\Develop\Claude\opencode-tracker\screenshot_debug.txt", $"Configs: {groupedConfigs.Count}"); } catch {}

            foreach (var config in groupedConfigs)
            {
                var usage = usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));

                // Card Container
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(10, 8, 10, 8)
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Inputs

                // Header: Icon + Name
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                
                var icon = new Image
                {
                    Source = GetIconForProvider(config.ProviderId),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(icon);

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
                    _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("_", " ").Replace("-", " "))
                };

                var title = new TextBlock
                {
                    Text = displayName,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 120
                };
                headerPanel.Children.Add(title);

                // Add tray checkbox to header
                var trayCheckBox = new CheckBox
                {
                    Content = "Tray",
                    IsChecked = config.ShowInTray,
                    Foreground = Brushes.LightGray,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                trayCheckBox.Checked += (s, e) => {
                    config.ShowInTray = true;
                    ((App)Application.Current).UpdateProviderTrayIcons(_providerManager.LastUsages, _configs, _prefs);
                };
                trayCheckBox.Unchecked += (s, e) => {
                    config.ShowInTray = false;
                    ((App)Application.Current).UpdateProviderTrayIcons(_providerManager.LastUsages, _configs, _prefs);
                };
                headerPanel.Children.Add(trayCheckBox);

                // Add notification checkbox to header
                var notifyCheckBox = new CheckBox
                {
                    Content = "Notify",
                    IsChecked = config.EnableNotifications,
                    Foreground = Brushes.LightGray,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                notifyCheckBox.Checked += (s, e) => {
                    config.EnableNotifications = true;
                };
                notifyCheckBox.Unchecked += (s, e) => {
                    config.EnableNotifications = false;
                };
                headerPanel.Children.Add(notifyCheckBox);

                if (usage != null && !usage.IsAvailable)
                {
                    var status = new Border 
                    { 
                        Background = new SolidColorBrush(Color.FromArgb(50, 255, 100, 100)),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(10,0,0,0),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    status.Child = new TextBlock { Text = "Inactive", FontSize=10, Foreground=Brushes.LightCoral };
                    headerPanel.Children.Add(status);
                }

                grid.Children.Add(headerPanel);

                // Inputs: API Key or Auth Button (no label)
                var keyPanel = new Grid { Margin = new Thickness(0,0,0,0) };
                keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                if (config.ProviderId == "github-copilot")
                {
                    // Special GitHub Login UI (no label)
                    var authBox = new StackPanel { Orientation = Orientation.Horizontal };
                    var displayUsername = _githubUsername;
                    if ((_prefs.IsPrivacyMode || _isScreenshotMode) && !string.IsNullOrEmpty(displayUsername))
                    {
                        displayUsername = PrivacyHelper.MaskString(displayUsername);
                    }

                    var authStatusText = _githubAuthService.IsAuthenticated 
                        ? (string.IsNullOrEmpty(displayUsername) ? "Authenticated" : $"Authenticated ({displayUsername})")
                        : "Not Authenticated";

                    var authStatus = new TextBlock 
                    { 
                        Text = authStatusText, 
                        Foreground = _githubAuthService.IsAuthenticated ? Brushes.LightGreen : Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                        FontSize = 11
                    };

                    var authBtn = new Button 
                    { 
                        Content = _githubAuthService.IsAuthenticated ? "Log out" : "Log in",
                        Padding = new Thickness(10, 2, 10, 2),
                        FontSize = 11,
                        Background = new SolidColorBrush(Color.FromRgb(50, 50, 50))
                    };

                    authBtn.Click += (s, e) => 
                    {
                        if (_githubAuthService.IsAuthenticated)
                        {
                            _githubAuthService.Logout();
                            config.ApiKey = ""; // Clear key
                            authStatus.Text = "Not Authenticated";
                            authStatus.Foreground = Brushes.Gray;
                            authBtn.Content = "Log in";
                            PopulateList(); // Re-render to update UI consistency if needed
                        }
                        else
                        {
                            var dialog = new GitHubLoginDialog(_githubAuthService);
                            dialog.Owner = this;
                            if (dialog.ShowDialog() == true)
                            {
                                var token = _githubAuthService.GetCurrentToken();
                                if (!string.IsNullOrEmpty(token)) config.ApiKey = token;
                                
                                // Fetch username after login
                                _ = Task.Run(async () => {
                                    _githubUsername = await _githubAuthService.GetUsernameAsync();
                                    Dispatcher.Invoke(() => PopulateList());
                                });
                            }
                        }
                    };

                    authBox.Children.Add(authStatus);
                    authBox.Children.Add(authBtn);

                    Grid.SetColumn(authBox, 0);
                    keyPanel.Children.Add(authBox);
                }
                else if (config.ProviderId == "antigravity")
                {
                    // Antigravity: Local Process Auto-Detection (No Key Input)
                    var statusPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    
                    bool isConnected = usage != null && usage.IsAvailable;
                    string accountInfo = usage?.AccountName ?? "Unknown";

                    if ((_prefs.IsPrivacyMode || _isScreenshotMode) && !string.IsNullOrEmpty(accountInfo) && accountInfo != "Unknown")
                    {
                        accountInfo = PrivacyHelper.MaskString(accountInfo);
                    }

                    var statusText = new TextBlock
                    {
                        Text = isConnected ? $"Auto-Detected ({accountInfo})" : "Searching for local process...",
                        Foreground = isConnected ? Brushes.LightGreen : Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 11,
                        FontStyle = isConnected ? FontStyles.Normal : FontStyles.Italic
                    };

                    statusPanel.Children.Add(statusText);
                    Grid.SetColumn(statusPanel, 0);
                    keyPanel.Children.Add(statusPanel);
                }
                else
                {
                    // Standard API Key Input (no label)
                    var displayKey = config.ApiKey;
                    bool shouldMask = _isScreenshotMode || _prefs.IsPrivacyMode;
                    if (shouldMask && !string.IsNullOrEmpty(displayKey))
                    {
                        // Mask the key significantly
                        if (displayKey.Length > 8)
                            displayKey = displayKey.Substring(0, 4) + "****************" + displayKey.Substring(displayKey.Length - 4);
                        else
                            displayKey = "********";
                    }

                    var keyBox = new TextBox
                    {
                        Text = displayKey,
                        Tag = config,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        FontSize = 11,
                        IsReadOnly = _isScreenshotMode || _prefs.IsPrivacyMode
                    };
                    if (!_isScreenshotMode && !_prefs.IsPrivacyMode)
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
                
                // Sub-Quotas for Antigravity (Special Case)
                if (config.ProviderId.Equals("antigravity", StringComparison.OrdinalIgnoreCase) && usage?.Details != null)
                {
                    var separator = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(60,60,60)), Margin = new Thickness(0, 10, 0, 10) };
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    
                    Grid.SetRow(separator, 3);
                    grid.Children.Add(separator);

                    var subPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                    var subTitle = new TextBlock { Text = "Individual Quota Icons:", Foreground = Brushes.Gray, FontSize = 11, FontWeight=FontWeights.SemiBold, Margin = new Thickness(0,0,0,5) };
                    subPanel.Children.Add(subTitle);

                    foreach (var detail in usage.Details)
                    {
                        var subCheck = new CheckBox 
                        {
                            Content = detail.Name,
                            IsChecked = config.EnabledSubTrays.Contains(detail.Name),
                            Foreground = Brushes.LightGray,
                            FontSize = 11,
                            Margin = new Thickness(0, 2, 0, 2),
                            Cursor = System.Windows.Input.Cursors.Hand
                        };
                        subCheck.Checked += (s, e) => {
                            if (!config.EnabledSubTrays.Contains(detail.Name)) config.EnabledSubTrays.Add(detail.Name);
                             ((App)Application.Current).UpdateProviderTrayIcons(_providerManager.LastUsages, _configs, _prefs);
                        };
                        subCheck.Unchecked += (s, e) => {
                            config.EnabledSubTrays.Remove(detail.Name);
                             ((App)Application.Current).UpdateProviderTrayIcons(_providerManager.LastUsages, _configs, _prefs);
                        };
                        subPanel.Children.Add(subCheck);
                    }
                    
                    Grid.SetRow(subPanel, 4);
                    grid.Children.Add(subPanel);
                }

                card.Child = grid;
                ProvidersStack.Children.Add(card);
            }
        }

        private ImageSource GetIconForProvider(string providerId)
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
                    "minimax-io" => "minimax",
                    "minimax-global" => "minimax",
                    "minimax" => "minimax",
                    "kimi" => "kimi",
                    "xiaomi" => "xiaomi",
                    _ => providerId.ToLower()
                };

                var appDir = AppDomain.CurrentDomain.BaseDirectory;

                var svgPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.svg");
                if (System.IO.File.Exists(svgPath))
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
                        return image;
                    }
                }

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

            return new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/usage_icon.png"));
        }

        private void PopulateLayout()
        {
             // PRE-CREATE PREVIEW ELEMENT
             var previewText = new TextBlock
             {
                  Text = "OpenAI: $15.00 / $100.00 (15%)",
                  FontSize = _prefs.FontSize > 0 ? _prefs.FontSize : 12,
                  Foreground = Brushes.White,
                  VerticalAlignment = VerticalAlignment.Center
             };
             // Initial state
             try {
                if (!string.IsNullOrEmpty(_prefs.FontFamily))
                    previewText.FontFamily = new System.Windows.Media.FontFamily(_prefs.FontFamily);
                previewText.FontWeight = _prefs.FontBold ? FontWeights.Bold : FontWeights.Normal;
                previewText.FontStyle = _prefs.FontItalic ? FontStyles.Italic : FontStyles.Normal;
             } catch {}

             // Helper to update preview
             void UpdatePreview()
             {
                 try
                 {
                     if (!string.IsNullOrEmpty(_prefs.FontFamily))
                         previewText.FontFamily = new System.Windows.Media.FontFamily(_prefs.FontFamily);
                     previewText.FontSize = _prefs.FontSize > 0 ? _prefs.FontSize : 12;
                     previewText.FontWeight = _prefs.FontBold ? FontWeights.Bold : FontWeights.Normal;
                     previewText.FontStyle = _prefs.FontItalic ? FontStyles.Italic : FontStyles.Normal;
                 }
                 catch { }
             }

             // Add UI Colors Section
             var header = new TextBlock { Text = "UI Colors", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray, Margin = new Thickness(0, 20, 0, 10) };
             LayoutStack.Children.Add(header);

             var grid = new Grid();
             grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
             grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
             grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
             
             // Yellow Threshold
             var lblYellow = new TextBlock { Text = "Yellow Threshold (%)", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
             var txtYellow = new TextBox 
             { 
                 Text = _prefs.ColorThresholdYellow.ToString(), 
                 Width = 50, 
                 Height = 24,
                 Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                 Foreground = Brushes.White,
                 BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                 VerticalContentAlignment = VerticalAlignment.Center
             };
             txtYellow.TextChanged += (s, e) => {
                 if (int.TryParse(txtYellow.Text, out var val)) _prefs.ColorThresholdYellow = val;
             };
             
              Grid.SetColumn(lblYellow, 0);
              Grid.SetColumn(txtYellow, 1);
              grid.Children.Add(lblYellow);
              grid.Children.Add(txtYellow);

              LayoutStack.Children.Add(grid);

              // Red Threshold
              var grid2 = new Grid { Margin = new Thickness(0, 10, 0, 0) };
              grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
              grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
              grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var lblRed = new TextBlock { Text = "Red Threshold (%)", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var txtRed = new TextBox
              {
                  Text = _prefs.ColorThresholdRed.ToString(),
                  Width = 50,
                  Height = 24,
                  Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                  Foreground = Brushes.White,
                  BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                  VerticalContentAlignment = VerticalAlignment.Center
              };
              txtRed.TextChanged += (s, e) => {
                  if (int.TryParse(txtRed.Text, out var val)) _prefs.ColorThresholdRed = val;
              };

              Grid.SetColumn(lblRed, 0);
              Grid.SetColumn(txtRed, 1);
              grid2.Children.Add(lblRed);
              grid2.Children.Add(txtRed);

              LayoutStack.Children.Add(grid2);

              // Invert Progress Bar Checkbox
              var invertCheck = new CheckBox
              {
                  Content = "Invert Progress Bars (Show 'Remaining' instead of 'Used')",
                  IsChecked = _prefs.InvertProgressBar,
                  Foreground = Brushes.LightGray,
                  FontSize = 11,
                  Margin = new Thickness(0, 15, 0, 0),
                  VerticalAlignment = VerticalAlignment.Center
              };
              invertCheck.Checked += (s, e) => _prefs.InvertProgressBar = true;
              invertCheck.Unchecked += (s, e) => _prefs.InvertProgressBar = false;

              LayoutStack.Children.Add(invertCheck);
              
              // Auto Refresh Interval
              var gridRefresh = new Grid { Margin = new Thickness(0, 15, 0, 0) };
              gridRefresh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
              gridRefresh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
              gridRefresh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var lblRefresh = new TextBlock { Text = "Auto Refresh (Minutes)", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var txtRefresh = new TextBox
              {
                  Text = (_prefs.AutoRefreshInterval / 60).ToString(),
                  Width = 50,
                  Height = 24,
                  Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                  Foreground = Brushes.White,
                  BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                  VerticalContentAlignment = VerticalAlignment.Center,
                  ToolTip = "Set to 0 to disable automatic refresh"
              };
              txtRefresh.TextChanged += (s, e) => {
                  if (int.TryParse(txtRefresh.Text, out var val)) _prefs.AutoRefreshInterval = val * 60;
              };

              Grid.SetColumn(lblRefresh, 0);
               Grid.SetColumn(txtRefresh, 1);
               gridRefresh.Children.Add(lblRefresh);
                gridRefresh.Children.Add(txtRefresh);

                LayoutStack.Children.Add(gridRefresh);

                // Notifications Section
                var notifHeader = new TextBlock { Text = "Notifications", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray, Margin = new Thickness(0, 20, 0, 10) };
                LayoutStack.Children.Add(notifHeader);

                var notifCheck = new CheckBox 
                { 
                    Content = "Enable Windows notifications for quota events",
                    IsChecked = _prefs.EnableNotifications,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 5, 0, 5)
                };
                notifCheck.Checked += (s, e) => _prefs.EnableNotifications = true;
                notifCheck.Unchecked += (s, e) => _prefs.EnableNotifications = false;
                LayoutStack.Children.Add(notifCheck);

                var notifDesc = new TextBlock 
                { 
                    Text = "Show notifications when quotas are depleted or refreshed",
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    Margin = new Thickness(20, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                };
                LayoutStack.Children.Add(notifDesc);

                // Separator
                var separator = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Margin = new Thickness(0, 30, 0, 10) };
               LayoutStack.Children.Add(separator);

              // Font Settings Section
              var fontHeaderPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
              var fontHeader = new TextBlock { Text = "Font Settings", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
              var resetBtn = new Button { Content = "Reset to Default", FontSize = 10, Padding = new Thickness(6,2,6,2), HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(60,60,60)) };
              resetBtn.Click += ResetFontBtn_Click;
              
              fontHeaderPanel.Children.Add(fontHeader);
              DockPanel.SetDock(fontHeader, Dock.Left);
              fontHeaderPanel.Children.Add(resetBtn);
              DockPanel.SetDock(resetBtn, Dock.Right);
              
              LayoutStack.Children.Add(fontHeaderPanel);

              // Font Family
              var fontFamilyGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
              fontFamilyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
              fontFamilyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var fontFamilyLabel = new TextBlock { Text = "Font Family", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var fontFamilyBox = new ComboBox
              {
                  Height = 24,
                  Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                  Foreground = Brushes.White,
                  BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                  IsEditable = false
              };
              
              // Populate fonts
              var fonts = _fontProvider.GetInstalledFonts().ToList();
              fontFamilyBox.ItemsSource = fonts;
              
              // Select current preference
              var selectedFont = FontSelectionHelper.GetSelectedFont(_prefs.FontFamily, fonts);
              fontFamilyBox.SelectedItem = selectedFont;

              fontFamilyBox.SelectionChanged += (s, e) => {
                  if (fontFamilyBox.SelectedItem is string font)
                  {
                      _prefs.FontFamily = font;
                      UpdatePreview();
                  }
              };

              Grid.SetColumn(fontFamilyLabel, 0);
              Grid.SetColumn(fontFamilyBox, 1);
              fontFamilyGrid.Children.Add(fontFamilyLabel);
              fontFamilyGrid.Children.Add(fontFamilyBox);
              LayoutStack.Children.Add(fontFamilyGrid);

              // Font Size
              var fontSizeGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
              fontSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
              fontSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
              fontSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var fontSizeLabel = new TextBlock { Text = "Font Size (px)", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var fontSizeBox = new TextBox
              {
                  Text = _prefs.FontSize.ToString(),
                  Width = 50,
                  Height = 24,
                  Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                  Foreground = Brushes.White,
                  BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                  VerticalContentAlignment = VerticalAlignment.Center
              };
              fontSizeBox.TextChanged += (s, e) => {
                  if (int.TryParse(fontSizeBox.Text, out var val)) 
                  {
                      _prefs.FontSize = val;
                      UpdatePreview();
                  }
              };

              Grid.SetColumn(fontSizeLabel, 0);
              Grid.SetColumn(fontSizeBox, 1);
              fontSizeGrid.Children.Add(fontSizeLabel);
              fontSizeGrid.Children.Add(fontSizeBox);
              LayoutStack.Children.Add(fontSizeGrid);

              // Font Style Options
              var fontStylePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(100, 0, 0, 0) };

              var boldCheck = new CheckBox
              {
                  Content = "Bold",
                  IsChecked = _prefs.FontBold,
                  Foreground = Brushes.LightGray,
                  FontSize = 11,
                  VerticalAlignment = VerticalAlignment.Center,
                  Margin = new Thickness(0, 0, 15, 0)
              };
              boldCheck.Checked += (s, e) => { _prefs.FontBold = true; UpdatePreview(); };
              boldCheck.Unchecked += (s, e) => { _prefs.FontBold = false; UpdatePreview(); };
              fontStylePanel.Children.Add(boldCheck);

              var italicCheck = new CheckBox
              {
                  Content = "Italic",
                  IsChecked = _prefs.FontItalic,
                  Foreground = Brushes.LightGray,
                  FontSize = 11,
                  VerticalAlignment = VerticalAlignment.Center
              };
              italicCheck.Checked += (s, e) => { _prefs.FontItalic = true; UpdatePreview(); };
              italicCheck.Unchecked += (s, e) => { _prefs.FontItalic = false; UpdatePreview(); };
              fontStylePanel.Children.Add(italicCheck);

              LayoutStack.Children.Add(fontStylePanel);

              // Preview Text
              var previewLabel = new TextBlock { Text = "Preview:", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray, Margin = new Thickness(0, 20, 0, 10) };
              LayoutStack.Children.Add(previewLabel);

              var previewBox = new Border
              {
                  Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                  BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                  BorderThickness = new Thickness(1),
                  CornerRadius = new CornerRadius(4),
                  Padding = new Thickness(12),
                  Margin = new Thickness(0, 0, 0, 20)
              };

              previewBox.Child = previewText;
              LayoutStack.Children.Add(previewBox);

        }

        private void ResetFontBtn_Click(object sender, RoutedEventArgs e)
        {
             // Reset Preferences
             _prefs.FontFamily = "Segoe UI";
             _prefs.FontSize = 12;
             _prefs.FontBold = false;
             _prefs.FontItalic = false;

             // Update UI - Re-populate layout to reflect changes is hardest, easier to just update controls if we had references.
             // Since controls are created in code-behind without field references, we can clear and rebuild LayoutStack.
             LayoutStack.Children.Clear();
             PopulateLayout();
        }

        private async void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            btn.IsEnabled = false;
            btn.Content = "Scanning...";

            try
            {
                // LoadConfigAsync does discovery + loading from all known auth.json files
                var discoveredConfigs = await _configLoader.LoadConfigAsync();
                
                // Merge discovered keys into current _configs (don't overwrite if existing has a key)
                foreach(var dc in discoveredConfigs)
                {
                    var existing = _configs.FirstOrDefault(c => c.ProviderId == dc.ProviderId);
                    if (existing == null)
                    {
                        _configs.Add(dc);
                    }
                    else if (string.IsNullOrEmpty(existing.ApiKey) && !string.IsNullOrEmpty(dc.ApiKey))
                    {
                        existing.ApiKey = dc.ApiKey;
                        if (string.IsNullOrEmpty(existing.BaseUrl)) existing.BaseUrl = dc.BaseUrl;
                    }
                }

                // Force a fresh fetch of usage data from all providers
                await _providerManager.GetAllUsageAsync(true);

                PopulateList();
            }
            finally
            {
                btn.Content = "Scan for Keys";
                btn.IsEnabled = true;
            }
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            await _configLoader.SaveConfigAsync(_configs);
            await _configLoader.SavePreferencesAsync(_prefs);
            SettingsChanged = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void PrivacyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
            {
                await app.TogglePrivacyMode();
            }
        }

        private void UpdatePrivacyButton()
        {
            if (_prefs.IsPrivacyMode)
            {
                PrivacyBtn.Foreground = Brushes.Gold;
            }
            else
            {
                PrivacyBtn.Foreground = Brushes.Gray;
            }
        }

        private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var updateInfo = await _updateChecker.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    System.Windows.MessageBox.Show($"New version available: {updateInfo.Version}", "Update Available", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("You're already on the latest version.", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to check for updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}

