using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;

namespace AIConsumptionTracker.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly IConfigLoader _configLoader;
        private readonly ProviderManager _providerManager;
        private List<ProviderConfig> _configs = new();
        private AppPreferences _prefs = new();

        public bool SettingsChanged { get; private set; }

        public SettingsWindow(IConfigLoader configLoader, ProviderManager providerManager)
        {
            InitializeComponent();
            _configLoader = configLoader;
            _providerManager = providerManager;
            Loaded += SettingsWindow_Loaded;
        }

        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _configs = await _configLoader.LoadConfigAsync();
            _prefs = await _configLoader.LoadPreferencesAsync();
            PopulateList();
            PopulateLayout();
        }

        private void PopulateList()
        {
            ProvidersStack.Children.Clear();

            // Get current usage to show dynamic bars
            var usages = _providerManager.LastUsages;

            var groupedConfigs = _configs.OrderBy(c => c.ProviderId).ToList();

            foreach (var config in groupedConfigs)
            {
                var usage = usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));

                // Card Container
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(12)
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Inputs
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Options

                // Header: Icon + Name
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                
                var icon = new Image
                {
                    Source = GetIconForProvider(config.ProviderId),
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(icon);

                var displayName = config.ProviderId switch {
                    "antigravity" => "Google Antigravity",
                    "gemini-cli" => "Google Gemini",
                    "github-copilot" => "GitHub Copilot",
                    _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("_", " ").Replace("-", " "))
                };

                var title = new TextBlock
                {
                    Text = displayName,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(title);

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

                // Inputs: API Key
                var keyPanel = new Grid { Margin = new Thickness(0,0,0,8) };
                keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var keyLabel = new TextBlock { Text = "API Key", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
                
                var keyBox = new TextBox
                {
                    Text = config.ApiKey,
                    Tag = config,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                keyBox.TextChanged += (s, e) => {
                    config.ApiKey = keyBox.Text;
                };

                Grid.SetColumn(keyLabel, 0);
                Grid.SetColumn(keyBox, 1);
                keyPanel.Children.Add(keyLabel);
                keyPanel.Children.Add(keyBox);

                Grid.SetRow(keyPanel, 1);
                grid.Children.Add(keyPanel);

                // Options
                var optionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(80, 0, 0, 0) }; // Indent to match input
                
                var trayCheckBox = new CheckBox
                {
                    Content = "Show in System Tray",
                    IsChecked = config.ShowInTray,
                    Foreground = Brushes.LightGray,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                trayCheckBox.Checked += (s, e) => {
                    config.ShowInTray = true;
                    ((App)Application.Current).UpdateProviderTrayIcons(_providerManager.LastUsages, _configs, _prefs);
                };
                trayCheckBox.Unchecked += (s, e) => {
                    config.ShowInTray = false;
                    ((App)Application.Current).UpdateProviderTrayIcons(_providerManager.LastUsages, _configs, _prefs);
                };
                optionsPanel.Children.Add(trayCheckBox);

                Grid.SetRow(optionsPanel, 2);
                grid.Children.Add(optionsPanel);
                
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
                // Map provider IDs to icon filenames
                string filename = providerId.ToLower() switch
                {
                    "github-copilot" => "github",
                    "google-gemini" => "google",
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
                        image.Freeze(); // Make it thread-safe and immutable
                        return image;
                    }
                }
            } 
            catch { }

            // Fallback to default PNG
            return new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/usage_icon.png"));
        }

        private void PopulateLayout()
        {
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

              // Separator
              var separator = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Margin = new Thickness(0, 30, 0, 10) };
              LayoutStack.Children.Add(separator);

              // Font Settings Section
              var fontHeader = new TextBlock { Text = "Font Settings", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 10) };
              LayoutStack.Children.Add(fontHeader);

              // Font Family
              var fontFamilyGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
              fontFamilyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
              fontFamilyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

              var fontFamilyLabel = new TextBlock { Text = "Font Family", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
              var fontFamilyBox = new ComboBox
              {
                  Text = _prefs.FontFamily,
                  Height = 24,
                  Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                  Foreground = Brushes.White,
                  BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60))
              };
              fontFamilyBox.Items.Add("Segoe UI");
              fontFamilyBox.Items.Add("Arial");
              fontFamilyBox.Items.Add("Calibri");
              fontFamilyBox.Items.Add("Consolas");
              fontFamilyBox.Items.Add("Microsoft Sans Serif");
              fontFamilyBox.Items.Add("Tahoma");
              fontFamilyBox.Items.Add("Trebuchet MS");
              fontFamilyBox.Items.Add("Verdana");
              fontFamilyBox.Items.Add("Lucida Console");
              fontFamilyBox.IsEditable = true;
              fontFamilyBox.SelectionChanged += (s, e) => {
                  _prefs.FontFamily = fontFamilyBox.Text ?? "Segoe UI";
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
                  if (int.TryParse(fontSizeBox.Text, out var val)) _prefs.FontSize = val;
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
              boldCheck.Checked += (s, e) => _prefs.FontBold = true;
              boldCheck.Unchecked += (s, e) => _prefs.FontBold = false;
              fontStylePanel.Children.Add(boldCheck);

              var italicCheck = new CheckBox
              {
                  Content = "Italic",
                  IsChecked = _prefs.FontItalic,
                  Foreground = Brushes.LightGray,
                  FontSize = 11,
                  VerticalAlignment = VerticalAlignment.Center
              };
              italicCheck.Checked += (s, e) => _prefs.FontItalic = true;
              italicCheck.Unchecked += (s, e) => _prefs.FontItalic = false;
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

              var previewText = new TextBlock
              {
                  Text = "OpenAI: $15.00 / $100.00 (15%)",
                  FontSize = _prefs.FontSize,
                  Foreground = Brushes.White,
                  VerticalAlignment = VerticalAlignment.Center
              };
              previewText.SetResourceReference(TextBlock.FontFamilyProperty, SystemFonts.MessageFontFamilyKey);
              previewText.FontFamily = new System.Windows.Media.FontFamily(_prefs.FontFamily);
              previewText.FontWeight = _prefs.FontBold ? FontWeights.Bold : FontWeights.Normal;
              previewText.FontStyle = _prefs.FontItalic ? FontStyles.Italic : FontStyles.Normal;

              previewBox.Child = previewText;
              LayoutStack.Children.Add(previewBox);
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
    }
}

