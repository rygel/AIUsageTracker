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
            PopulateThresholds();
        }

        private void PopulateList()
        {
            SettingsStack.Children.Clear();

            // Get current usage to show dynamic bars
            var usages = _providerManager.LastUsages;

            foreach (var config in _configs.OrderBy(c => c.ProviderId))
            {
                var usage = usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));

                var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 15) };
                rowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: Header & Key
                rowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: Tray Toggle
                rowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: Sub-Quotas

                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); // Icon
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // Label width
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Field width

                // Row 0: Icon and Name
                var icon = new Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/usage_icon.png")),
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = usage != null ? 1.0 : 0.4
                };
                Grid.SetColumn(icon, 0);
                rowGrid.Children.Add(icon);

                var displayName = config.ProviderId switch {
                    "antigravity" => "Google Antigravity",
                    "gemini-cli" => "Google Gemini",
                    _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("_", " ").Replace("-", " "))
                };

                var title = new TextBlock
                {
                    Text = displayName,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = usage != null ? Brushes.White : Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11
                };
                Grid.SetColumn(title, 1);
                rowGrid.Children.Add(title);

                var keyBox = new TextBox
                {
                    Text = config.ApiKey,
                    Tag = config,
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                    Padding = new Thickness(8, 4, 8, 4),
                    Height = 26,
                    FontSize = 11,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(10, 0, 0, 0)
                };
                
                keyBox.TextChanged += (s, e) => {
                    config.ApiKey = keyBox.Text;
                };

                Grid.SetRow(keyBox, 0);
                Grid.SetColumn(keyBox, 2);
                rowGrid.Children.Add(keyBox);

                // Track in Tray Checkbox
                var trayCheckBox = new CheckBox
                {
                    Content = "Track Summary in Tray",
                    IsChecked = config.ShowInTray,
                    Foreground = Brushes.Gray,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                trayCheckBox.Checked += (s, e) => {
                    config.ShowInTray = true;
                    ((App)Application.Current).UpdateProviderTrayIcons(_providerManager.LastUsages, _configs, _prefs);
                };
                trayCheckBox.Unchecked += (s, e) => {
                    config.ShowInTray = false;
                    ((App)Application.Current).UpdateProviderTrayIcons(_providerManager.LastUsages, _configs, _prefs);
                };

                Grid.SetRow(trayCheckBox, 1);
                Grid.SetColumn(trayCheckBox, 1);
                rowGrid.Children.Add(trayCheckBox);

                // Sub-Quotas for Antigravity
                if (config.ProviderId.Equals("antigravity", StringComparison.OrdinalIgnoreCase) && usage?.Details != null)
                {
                    var subPanel = new StackPanel { Margin = new Thickness(10, 5, 0, 5) };
                    var subTitle = new TextBlock { Text = "Individual Quota Icons:", Foreground = Brushes.DimGray, FontSize = 9, Margin = new Thickness(0,0,0,3) };
                    subPanel.Children.Add(subTitle);

                    foreach (var detail in usage.Details)
                    {
                        var subCheck = new CheckBox 
                        {
                            Content = detail.Name,
                            IsChecked = config.EnabledSubTrays.Contains(detail.Name),
                            Foreground = Brushes.LightGray,
                            FontSize = 9,
                            Margin = new Thickness(0, 1, 0, 1)
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
                    
                    Grid.SetRow(subPanel, 2);
                    Grid.SetColumn(subPanel, 1);
                    Grid.SetColumnSpan(subPanel, 2);
                    rowGrid.Children.Add(subPanel);
                }
                
                SettingsStack.Children.Add(rowGrid);
            }
        }

        private void PopulateThresholds()
        {
             // Add UI Colors Section
             var header = new TextBlock { Text = "UI Colors", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray, Margin = new Thickness(0, 20, 0, 10) };
             SettingsStack.Children.Add(header);

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
             
             SettingsStack.Children.Add(grid);

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
             
             SettingsStack.Children.Add(grid2);
             
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
             
             SettingsStack.Children.Add(invertCheck);
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
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

