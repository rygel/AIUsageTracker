using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Core.AgentClient;
using AIConsumptionTracker.Infrastructure.Helpers;
using AIConsumptionTracker.UI.Services;
using System.Windows.Input;

namespace AIConsumptionTracker.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly IConfigLoader _configLoader;
        private readonly ProviderManager _providerManager;
        private readonly IFontProvider _fontProvider;
        private readonly IUpdateCheckerService _updateChecker;
        private readonly IGitHubAuthService _githubAuthService;
        private readonly AgentService _agentService;
        private List<ProviderConfig> _configs = new();
        private AppPreferences _prefs = new();

        public bool SettingsChanged { get; private set; }
        private bool _isScreenshotMode = false;
        private string? _githubUsername;

        public SettingsWindow(IConfigLoader configLoader, ProviderManager providerManager, IFontProvider fontProvider, IUpdateCheckerService updateChecker, IGitHubAuthService githubAuthService, AgentService agentService)
        {
            Debug.WriteLine("[DEBUG] SettingsWindow constructor started");
            try
            {
                InitializeComponent();
                Debug.WriteLine("[DEBUG] InitializeComponent completed successfully");
                
                _configLoader = configLoader;
                _providerManager = providerManager;
                _fontProvider = fontProvider;
                _updateChecker = updateChecker;
                _githubAuthService = githubAuthService;
                _agentService = agentService;
                
                Debug.WriteLine("[DEBUG] Dependencies assigned");
                
                Loaded += SettingsWindow_Loaded;
                Debug.WriteLine("[DEBUG] SettingsWindow constructor completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] SettingsWindow constructor failed: {ex}");
                System.Windows.MessageBox.Show($"Failed to initialize Settings window:\n{ex.GetType().Name}: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
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
            Debug.WriteLine("[DEBUG] SettingsWindow_Loaded started");
            
            if (_isScreenshotMode) 
            {
                Debug.WriteLine("[DEBUG] Screenshot mode, skipping loaded logic");
                return;
            }

            try
            {
                Debug.WriteLine("[DEBUG] Loading configs...");
                _configs = await _configLoader.LoadConfigAsync();
                Debug.WriteLine($"[DEBUG] Loaded {_configs.Count} configs");
                
                Debug.WriteLine("[DEBUG] Loading preferences...");
                _prefs = await _configLoader.LoadPreferencesAsync();
                Debug.WriteLine("[DEBUG] Preferences loaded");

                // Listen for global privacy changes
                if (Application.Current is App app)
                {
                    Debug.WriteLine("[DEBUG] Subscribing to PrivacyChanged event");
                    app.PrivacyChanged += (s, isPrivate) => {
                        _prefs.IsPrivacyMode = isPrivate;
                        UpdatePrivacyButton();
                        PopulateList();
                    };
                }

                Debug.WriteLine("[DEBUG] Updating privacy button...");
                UpdatePrivacyButton();

                Debug.WriteLine("[DEBUG] Initializing GitHub auth...");
                await InitializeGitHubAuthAsync();

                Debug.WriteLine("[DEBUG] Populating list...");
                PopulateList();
                
                // Check if we have usage data, if not, trigger a refresh
                if (_providerManager.LastUsages.Count == 0)
                {
                    Debug.WriteLine("[DEBUG] No usage data available, triggering refresh...");
                    var usages = await Task.Run(async () => await _providerManager.GetAllUsageAsync(forceRefresh: true));
                    Debug.WriteLine($"[DEBUG] Refresh completed, got {usages.Count} usages, repopulating list...");
                    PopulateList();
                }
                
                Debug.WriteLine("[DEBUG] Populating layout...");
                PopulateLayout();
                
                Debug.WriteLine("[DEBUG] Applying theme...");
                ApplyTheme();
                
                Debug.WriteLine("[DEBUG] SettingsWindow_Loaded completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] SettingsWindow_Loaded failed: {ex}");
                System.Windows.MessageBox.Show($"Failed to load Settings:\n{ex.GetType().Name}: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Loading Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
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
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 120
                };
                title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                headerPanel.Children.Add(title);

                // Add tray checkbox to header
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
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                notifyCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
                notifyCheckBox.Checked += (s, e) => {
                    config.EnableNotifications = true;
                };
                notifyCheckBox.Unchecked += (s, e) => {
                    config.EnableNotifications = false;
                };
                headerPanel.Children.Add(notifyCheckBox);

                // Show "Inactive" badge if no API key is configured
                bool isInactive = string.IsNullOrEmpty(config.ApiKey);
                
                // Special cases for auto-auth or auto-detected providers
                if (config.ProviderId == "antigravity")
                {
                    isInactive = usage == null || !usage.IsAvailable;
                }
                else if (config.ProviderId == "github-copilot")
                {
                    isInactive = !_githubAuthService.IsAuthenticated;
                }

                if (isInactive)
                {
                    var status = new Border
                    {
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(10, 0, 0, 0),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    status.SetResourceReference(Border.BackgroundProperty, "InactiveBadge");

                    var badgeText = new TextBlock { Text = "Inactive", FontSize = 10 };
                    badgeText.SetResourceReference(TextBlock.ForegroundProperty, "InactiveBadgeText");
                    status.Child = badgeText;
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
                    
                    string authStatusText;
                    if (!_githubAuthService.IsAuthenticated)
                    {
                        authStatusText = "Not Authenticated";
                    }
                    else if ((_prefs.IsPrivacyMode || _isScreenshotMode) && !string.IsNullOrEmpty(displayUsername))
                    {
                        authStatusText = $"Authenticated ({PrivacyHelper.MaskContent(displayUsername, displayUsername)})";
                    }
                    else if (!string.IsNullOrEmpty(displayUsername))
                    {
                        authStatusText = $"Authenticated ({displayUsername})";
                    }
                    else
                    {
                        authStatusText = "Authenticated";
                    }

                    var authStatus = new TextBlock 
                    { 
                        Text = authStatusText, 
                        Foreground = _githubAuthService.IsAuthenticated ? (SolidColorBrush)Application.Current.Resources["ProgressBarGreen"] : (SolidColorBrush)Application.Current.Resources["TertiaryText"],
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                        FontSize = 11
                    };

                    var authBtn = new Button 
                    { 
                        Content = _githubAuthService.IsAuthenticated ? "Log out" : "Log in",
                        Padding = new Thickness(10, 2, 10, 2),
                        FontSize = 11,
                        Background = (SolidColorBrush)Application.Current.Resources["ControlBackground"]
                    };

                    authBtn.Click += (s, e) => 
                    {
                        if (_githubAuthService.IsAuthenticated)
                        {
                            _githubAuthService.Logout();
                            config.ApiKey = ""; // Clear key
                            authStatus.Text = "Not Authenticated";
                            authStatus.Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryText"];
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
                    string displayAccount = (_prefs.IsPrivacyMode || _isScreenshotMode)
                        ? PrivacyHelper.MaskContent(accountInfo, accountInfo)
                        : accountInfo;

                    var statusText = new TextBlock
                    {
                        Text = isConnected ? $"Auto-Detected ({displayAccount})" : "Searching for local process...",
                        Foreground = isConnected ? (SolidColorBrush)Application.Current.Resources["ProgressBarGreen"] : (SolidColorBrush)Application.Current.Resources["TertiaryText"],
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
                    var separator = new Border { Height = 1, Background = (SolidColorBrush)Application.Current.Resources["Separator"], Margin = new Thickness(0, 10, 0, 10) };
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    
                    Grid.SetRow(separator, 3);
                    grid.Children.Add(separator);

                    var subPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                    var subTitle = new TextBlock { Text = "Individual Quota Icons:", Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryText"], FontSize = 11, FontWeight=FontWeights.SemiBold, Margin = new Thickness(0,0,0,5) };
                    subPanel.Children.Add(subTitle);

                    foreach (var detail in usage.Details)
                    {
                        var subCheck = new CheckBox
                        {
                            Content = detail.Name,
                            IsChecked = config.EnabledSubTrays.Contains(detail.Name),
                            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
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
             var previewText = new TextBlock
             {
                  Text = "OpenAI: $15.00 / $100.00 (15%)",
                  FontSize = _prefs.FontSize > 0 ? _prefs.FontSize : 12,
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                  VerticalAlignment = VerticalAlignment.Center
             };

             try {
                if (!string.IsNullOrEmpty(_prefs.FontFamily))
                    previewText.FontFamily = new System.Windows.Media.FontFamily(_prefs.FontFamily);
                previewText.FontWeight = _prefs.FontBold ? FontWeights.Bold : FontWeights.Normal;
                previewText.FontStyle = _prefs.FontItalic ? FontStyles.Italic : FontStyles.Normal;
             } catch {}

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

             var header = new TextBlock { Text = "UI Colors", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryText"], Margin = new Thickness(0, 20, 0, 10) };
             LayoutStack.Children.Add(header);

             var grid = new Grid();
             grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
             grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
             grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

             var lblYellow = new TextBlock { Text = "Yellow Threshold (%)", Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
             var txtYellow = new TextBox
             {
                 Text = _prefs.ColorThresholdYellow.ToString(),
                 Width = 50,
                 Height = 24,
                 Background = (SolidColorBrush)Application.Current.Resources["InputBackground"],
                 Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                 BorderBrush = (SolidColorBrush)Application.Current.Resources["ControlBorder"],
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

              var grid2 = new Grid { Margin = new Thickness(0, 10, 0, 0) };
              grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
              grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
              grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var lblRed = new TextBlock { Text = "Red Threshold (%)", Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var txtRed = new TextBox
              {
                  Text = _prefs.ColorThresholdRed.ToString(),
                  Width = 50,
                  Height = 24,
                  Background = (SolidColorBrush)Application.Current.Resources["InputBackground"],
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                  BorderBrush = (SolidColorBrush)Application.Current.Resources["ControlBorder"],
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

              var invertCheck = new CheckBox
              {
                  Content = "Invert Progress Bars (Show 'Remaining' instead of 'Used')",
                  IsChecked = _prefs.InvertProgressBar,
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                  FontSize = 11,
                  Margin = new Thickness(0, 15, 0, 0),
                  VerticalAlignment = VerticalAlignment.Center
              };
              invertCheck.Checked += (s, e) => _prefs.InvertProgressBar = true;
              invertCheck.Unchecked += (s, e) => _prefs.InvertProgressBar = false;
              LayoutStack.Children.Add(invertCheck);

              var gridRefresh = new Grid { Margin = new Thickness(0, 15, 0, 0) };
              gridRefresh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
              gridRefresh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
              gridRefresh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var lblRefresh = new TextBlock { Text = "Auto Refresh (Minutes)", Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var txtRefresh = new TextBox
              {
                  Text = (_prefs.AutoRefreshInterval / 60).ToString(),
                  Width = 50,
                  Height = 24,
                  Background = (SolidColorBrush)Application.Current.Resources["InputBackground"],
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                  BorderBrush = (SolidColorBrush)Application.Current.Resources["ControlBorder"],
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

              var notifHeader = new TextBlock { Text = "Notifications", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryText"], Margin = new Thickness(0, 20, 0, 10) };
              LayoutStack.Children.Add(notifHeader);

              var notifCheck = new CheckBox
              {
                  Content = "Enable Windows notifications for quota events",
                  IsChecked = _prefs.EnableNotifications,
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                  Margin = new Thickness(0, 5, 0, 5)
              };
              notifCheck.Checked += (s, e) => _prefs.EnableNotifications = true;
              notifCheck.Unchecked += (s, e) => _prefs.EnableNotifications = false;
              LayoutStack.Children.Add(notifCheck);

              var notifDesc = new TextBlock
              {
                  Text = "Show notifications when quotas are depleted or refreshed",
                  Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryText"],
                  FontSize = 11,
                  Margin = new Thickness(20, 0, 0, 10),
                  TextWrapping = TextWrapping.Wrap
              };
              LayoutStack.Children.Add(notifDesc);

              var gridThreshold = new Grid { Margin = new Thickness(20, 0, 0, 0) };
              gridThreshold.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
              gridThreshold.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
              gridThreshold.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

              var lblThreshold = new TextBlock { Text = "Usage Threshold (%)", Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"], VerticalAlignment = VerticalAlignment.Center };
              var sliderThreshold = new Slider
              {
                  Minimum = 50,
                  Maximum = 100,
                  Value = _prefs.NotificationThreshold,
                  TickFrequency = 5,
                  IsSnapToTickEnabled = true,
                  VerticalAlignment = VerticalAlignment.Center,
                  Margin = new Thickness(10, 0, 10, 0)
              };
              var valThreshold = new TextBlock { Text = $"{_prefs.NotificationThreshold}%", Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryText"], VerticalAlignment = VerticalAlignment.Center, Width = 30 };

              sliderThreshold.ValueChanged += (s, e) => {
                  _prefs.NotificationThreshold = Math.Round(sliderThreshold.Value);
                  valThreshold.Text = $"{_prefs.NotificationThreshold}%";
                  SettingsChanged = true;
              };

              Grid.SetColumn(lblThreshold, 0);
              Grid.SetColumn(sliderThreshold, 1);
              Grid.SetColumn(valThreshold, 2);
              gridThreshold.Children.Add(lblThreshold);
              gridThreshold.Children.Add(sliderThreshold);
              gridThreshold.Children.Add(valThreshold);
              LayoutStack.Children.Add(gridThreshold);

              var separator = new Border { Height = 1, Background = (SolidColorBrush)Application.Current.Resources["Separator"], Margin = new Thickness(0, 30, 0, 10) };
              LayoutStack.Children.Add(separator);

              var fontHeaderPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
              var fontHeader = new TextBlock { Text = "Font Settings", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryText"], VerticalAlignment = VerticalAlignment.Center };
              var resetBtn = new Button { Content = "Reset to Default", FontSize = 10, Padding = new Thickness(6,2,6,2), HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderColor"] };
              resetBtn.Click += ResetFontBtn_Click;

              fontHeaderPanel.Children.Add(fontHeader);
              DockPanel.SetDock(fontHeader, Dock.Left);
              fontHeaderPanel.Children.Add(resetBtn);
              DockPanel.SetDock(resetBtn, Dock.Right);
              LayoutStack.Children.Add(fontHeaderPanel);

              var fontFamilyGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
              fontFamilyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
              fontFamilyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var fontFamilyLabel = new TextBlock { Text = "Font Family", Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var fontFamilyBox = new ComboBox
              {
                  Height = 24,
                  Background = (SolidColorBrush)Application.Current.Resources["ComboBoxBackground"],
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                  BorderBrush = (SolidColorBrush)Application.Current.Resources["ControlBorder"],
                  IsEditable = false
              };

              var fonts = _fontProvider.GetInstalledFonts().ToList();
              fontFamilyBox.ItemsSource = fonts;
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

              var fontSizeGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
              fontSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
              fontSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
              fontSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var fontSizeLabel = new TextBlock { Text = "Font Size (px)", Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var fontSizeBox = new TextBox
              {
                  Text = _prefs.FontSize.ToString(),
                  Width = 50,
                  Height = 24,
                  Background = (SolidColorBrush)Application.Current.Resources["InputBackground"],
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                  BorderBrush = (SolidColorBrush)Application.Current.Resources["ControlBorder"],
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

              var fontStylePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(100, 0, 0, 0) };

              var boldCheck = new CheckBox
              {
                  Content = "Bold",
                  IsChecked = _prefs.FontBold,
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
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
                  Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryText"],
                  FontSize = 11,
                  VerticalAlignment = VerticalAlignment.Center
              };
              italicCheck.Checked += (s, e) => { _prefs.FontItalic = true; UpdatePreview(); };
              italicCheck.Unchecked += (s, e) => { _prefs.FontItalic = false; UpdatePreview(); };
              fontStylePanel.Children.Add(italicCheck);

              LayoutStack.Children.Add(fontStylePanel);

              var previewLabel = new TextBlock { Text = "Preview:", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryText"], Margin = new Thickness(0, 20, 0, 10) };
              LayoutStack.Children.Add(previewLabel);

              var previewBox = new Border
              {
                  Background = (SolidColorBrush)Application.Current.Resources["PreviewBackground"],
                  BorderBrush = (SolidColorBrush)Application.Current.Resources["PreviewBorder"],
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
             ApplyTheme();
        }

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"usage_export_{DateTime.Now:yyyyMMdd}",
                DefaultExt = ".csv",
                Filter = "CSV Files (*.csv)|*.csv|JSON Files (*.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                string format = System.IO.Path.GetExtension(dialog.FileName).TrimStart('.').ToLower();
                if (format != "json") format = "csv";

                try 
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    var stream = await _agentService.ExportDataAsync(format, 30); // Default 30 days
                    if (stream != null)
                    {
                        using var fileStream = System.IO.File.Create(dialog.FileName);
                        await stream.CopyToAsync(fileStream);
                        MessageBox.Show("Export complete!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to export data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                     MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
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
                await Task.Run(async () => await _providerManager.GetAllUsageAsync(true));

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
                PrivacyBtn.Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryText"];
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

        private async void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            _prefs.Theme = _prefs.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            await _configLoader.SavePreferencesAsync(_prefs);
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            var isDark = _prefs.Theme == AppTheme.Dark;

            // Update theme button icon
            if (ThemeBtn != null)
                ThemeBtn.Content = isDark ? "🌙" : "☀️";

            // Switch the theme using the centralized helper
            ThemeHelper.ApplyTheme(_prefs);
        }

        private void ApplyThemeToWindow(Window window, bool isDark)
        {
            ThemeHelper.ApplyThemeToWindow(window, isDark);
        }
    }
}

