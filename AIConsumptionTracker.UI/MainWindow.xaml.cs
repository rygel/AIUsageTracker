using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using System.Threading.Tasks; 
using System.Reflection; 

namespace AIConsumptionTracker.UI
{
    public partial class MainWindow : Window
    {
        private readonly ProviderManager _providerManager;
        private readonly IConfigLoader _configLoader;
        private AppPreferences _preferences = new();
        private List<ProviderUsage> _cachedUsages = new();
        private int _resetDisplayMode = 0; // 0: Both, 1: Relative Only, 2: Absolute Only
        private readonly System.Windows.Threading.DispatcherTimer _resetTimer;

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

        public MainWindow(ProviderManager providerManager, IConfigLoader configLoader)
        {
            InitializeComponent();
            _providerManager = providerManager;
            _configLoader = configLoader;

            _resetTimer = new System.Windows.Threading.DispatcherTimer();
            _resetTimer.Interval = TimeSpan.FromSeconds(15);
            _resetTimer.Tick += (s, e) => {
                if (_cachedUsages != null && _cachedUsages.Count > 0)
                {
                    RenderUsages(_cachedUsages);
                }
            };
            _resetTimer.Start();
            
            Loaded += async (s, e) => {
                // Position window bottom right (moved from MainWindow_Loaded)
                var desktopWorkingArea = SystemParameters.WorkArea;
                this.Left = desktopWorkingArea.Right - this.Width - 10;
                this.Top = desktopWorkingArea.Bottom - this.Height - 10;

                _preferences = await _configLoader.LoadPreferencesAsync();
                ShowAllToggle.IsChecked = _preferences.ShowAll;
                StayOpenCheck.IsChecked = _preferences.StayOpen;
                AlwaysOnTopCheck.IsChecked = _preferences.AlwaysOnTop;
                CompactCheck.IsChecked = _preferences.CompactMode;
                
                this.Topmost = _preferences.AlwaysOnTop;
                
                var version = Assembly.GetEntryAssembly()?.GetName().Version;
                if (version != null)
                {
                    VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
                }

                await RefreshData(forceRefresh: false);
            };

            this.Deactivated += (s, e) => {
                // Only hide if the window is visible and enabled (not showing a modal dialog)
                // AND StayOpen is false
                if (this.IsVisible && this.IsEnabled && !_preferences.StayOpen)
                {
                    // If we have an open child window (Settings), don't hide!
                    foreach (Window win in this.OwnedWindows)
                    {
                        if (win.IsVisible) return;
                    }
                    
                    this.Hide();
                }
            };
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
            if (forceRefresh)
            {
                ProvidersList.Children.Clear();
                ProvidersList.Children.Add(new TextBlock 
                { 
                    Text = "Refreshing...", 
                    Foreground = Brushes.Gray, 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
            }

            var usages = await _providerManager.GetAllUsageAsync(forceRefresh);
            _cachedUsages = usages; // Cache the data
            
            // Update Individual Tray Icons
            var configs = await _configLoader.LoadConfigAsync();
            var app = (App)Application.Current;
            app.UpdateProviderTrayIcons(usages, configs, _preferences);
            
            RenderUsages(usages);
        }

        private void RenderUsages(List<ProviderUsage> usages)
        {
            ProvidersList.Children.Clear();

            bool showAll = ShowAllToggle?.IsChecked ?? true;
            var filteredUsages = usages
                .Where(u => showAll || (u.IsAvailable && !u.Description.Contains("not found", StringComparison.OrdinalIgnoreCase)))
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
                ProvidersList.Children.Add(CreateGroupHeader("Plans & Quotas", Brushes.DeepSkyBlue));
                RenderGroup(planItems);
            }

            if (payGoItems.Any())
            {
                // Add a bit of spacer before the next group if planItems existed
                if (planItems.Any()) ProvidersList.Children.Add(new Border { Height = 12 });
                
                ProvidersList.Children.Add(CreateGroupHeader("Pay As You Go", Brushes.MediumSeaGreen));
                RenderGroup(payGoItems);
            }
        }

        private void RenderGroup(List<ProviderUsage> groupUsages)
        {
            foreach (var usage in groupUsages)
            {
                // Render Parent
                if (_preferences.CompactMode) ProvidersList.Children.Add(CreateCompactItem(usage));
                else ProvidersList.Children.Add(CreateStandardItem(usage));

                // Render Children (Details)
                if (usage.Details != null && usage.Details.Count > 0)
                {
                    foreach (var detail in usage.Details)
                    {
                        double pct = 0;
                        if (double.TryParse(detail.Used.TrimEnd('%'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p)) 
                            pct = p;

                        var childUsage = new ProviderUsage 
                        {
                            ProviderId = usage.ProviderId,
                            ProviderName = detail.Name,
                            Description = detail.Description,
                            UsagePercentage = pct,
                            IsQuotaBased = true, // Treat as quota bar usually
                            IsAvailable = true,
                            AccountName = "", // Don't repeat account
                            NextResetTime = detail.NextResetTime,
                            PaymentType = usage.PaymentType // Inherit for rendering logic
                        };

                        if (_preferences.CompactMode) ProvidersList.Children.Add(CreateCompactItem(childUsage, isChild: true));
                        else ProvidersList.Children.Add(CreateStandardItem(childUsage, isChild: true));
                    }
                }
            }
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
                Opacity = 0.8
            };

            var line = new Border
            {
                Height = 1,
                Background = accent,
                Opacity = 0.2,
                VerticalAlignment = VerticalAlignment.Center
            };

            grid.Children.Add(text);
            Grid.SetColumn(line, 1);
            grid.Children.Add(line);

            return grid;
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
                Background = Brushes.Transparent // Ensure hit-testing works
            };

            // Layer 1: Background Progress Bar
            if ((usage.UsagePercentage > 0 || usage.IsQuotaBased) && !isMissing && !isError)
            {
                var pGrid = new Grid();
                var indicatorWidth = Math.Min(usage.UsagePercentage, 100);
                if (_preferences.InvertProgressBar) indicatorWidth = Math.Max(0, 100 - indicatorWidth);

                pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indicatorWidth, GridUnitType.Star) });
                pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star) });

                var fill = new Border
                {
                    Background = usage.UsagePercentage > _preferences.ColorThresholdRed ? Brushes.Crimson : (usage.UsagePercentage > _preferences.ColorThresholdYellow ? Brushes.Gold : Brushes.MediumSeaGreen),
                    Opacity = 0.25,
                    CornerRadius = new CornerRadius(2)
                };
                pGrid.Children.Add(fill);
                grid.Children.Add(pGrid);
            }
            else
            {
                 // Default background for non-quota items or errors
                 var bg = new Border 
                 { 
                     Background = new SolidColorBrush(Color.FromRgb(30,30,30)), 
                     CornerRadius = new CornerRadius(2) 
                 };
                 grid.Children.Add(bg);
            }

            // Layer 2: Content Overlay
            var contentPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 6, 0) };

            // Icon (Only for parent, or maybe small dot for child?)
            if (!isChild)
            {
                var icon = new Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/usage_icon.png")),
                    Width = 12,
                    Height = 12,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.8
                };
                contentPanel.Children.Add(icon);
                DockPanel.SetDock(icon, Dock.Left);
            }
            else
            {
                // Indentation spacer/icon for child
                var icon = new Border
                {
                    Width = 4, Height = 4, Background = Brushes.Gray, CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center
                };
                contentPanel.Children.Add(icon);
                DockPanel.SetDock(icon, Dock.Left);
            }

            // Right Side: Usage/Status (Added first so it's prioritized in limited space)
            var statusText = "";
            string resetText = "";
            Brush statusBrush = Brushes.Gray;

            if (isMissing) { statusText = "Key Missing"; statusBrush = Brushes.IndianRed; }
            else if (isError) { statusText = "Error"; statusBrush = Brushes.Red; }
            else if (isConsoleCheck) { statusText = "Check Console"; statusBrush = Brushes.Orange; }
            else 
            { 
                statusText = usage.Description;
                
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
                    Foreground = Brushes.Gold,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
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
                Margin = new Thickness(10, 0, 0, 0)
            };
            contentPanel.Children.Add(rightBlock);
            DockPanel.SetDock(rightBlock, Dock.Right);

            // Name (Added last, gets remaining space)
            var accountPart = string.IsNullOrWhiteSpace(usage.AccountName) ? "" : $" [{usage.AccountName}]";
            var nameBlock = new TextBlock
            {
                Text = $"{usage.ProviderName}{accountPart}",
                FontWeight = isChild ? FontWeights.Normal : FontWeights.SemiBold,
                FontSize = 11,
                Foreground = isMissing ? Brushes.Gray : Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = string.IsNullOrEmpty(usage.AuthSource) ? usage.AuthSource : $"{usage.ProviderName}{accountPart}" 
            };
            contentPanel.Children.Add(nameBlock);
            DockPanel.SetDock(nameBlock, Dock.Left);

            grid.Children.Add(contentPanel);
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
                Background = isChild ? new SolidColorBrush(Color.FromRgb(40, 40, 40)) : new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(isChild ? 20 : 0, 0, 0, 8),
                BorderBrush = isMissing || isError ? Brushes.Maroon : (isConsoleCheck ? Brushes.DarkOrange : new SolidColorBrush(Color.FromRgb(50, 50, 50))),
                BorderThickness = new Thickness(1),
                Opacity = (isMissing || !usage.IsAvailable) ? 0.6 : 1.0
            };

            // Special case for non-quota Child Items (e.g. Free Tier: Yes)
            if (isChild && (!usage.IsQuotaBased && usage.UsagePercentage <= 0.001))
            {
               var simpleGrid = new Grid();
               simpleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
               simpleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

               var nameTxt = new TextBlock
               {
                   Text = usage.ProviderName, // Actually the "Name" of detail
                   Foreground = Brushes.Silver,
                   FontSize = 12,
                   VerticalAlignment = VerticalAlignment.Center
               };
               
               // Indent
               var panel = new StackPanel { Orientation = Orientation.Horizontal };
               panel.Children.Add(new Border { Width=6, Height=6, Background=Brushes.Gray, CornerRadius=new CornerRadius(3), Margin=new Thickness(4,0,12,0), VerticalAlignment=VerticalAlignment.Center });
               panel.Children.Add(nameTxt);

               var valueTxt = new TextBlock
               {
                   Text = usage.Description,
                   Foreground = Brushes.White,
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
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/usage_icon.png")),
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.8
                };
                if (!isChild)
                {
                    headerGrid.Children.Add(icon);
                }
                else
                {
                    // Child Indent
                     var indent = new Border { Width=6, Height=6, Background=Brushes.Gray, CornerRadius=new CornerRadius(3), Margin=new Thickness(4,0,12,0), VerticalAlignment=VerticalAlignment.Center };
                     headerGrid.Children.Add(indent);
                }

                var accountPart = string.IsNullOrWhiteSpace(usage.AccountName) ? "" : $" [{usage.AccountName}]";
                var nameBlock = new TextBlock 
                { 
                    Text = $"{usage.ProviderName}{accountPart}", 
                    FontWeight = isChild ? FontWeights.Normal : FontWeights.SemiBold, 
                    FontSize = 13,
                    Foreground = isMissing ? Brushes.Gray : Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = string.IsNullOrEmpty(usage.AuthSource) ? null : usage.AuthSource
                };
            Grid.SetColumn(nameBlock, 1);
            headerGrid.Children.Add(nameBlock);

            if (isMissing || isConsoleCheck || isError)
            {
                var statusText = isMissing ? "API Key not found" : (isConsoleCheck ? "Check Console" : "[Error]");
                var statusBrush = isMissing ? Brushes.IndianRed : (isConsoleCheck ? Brushes.Orange : Brushes.Red);
                var statusBlock = new TextBlock { Text = statusText, Foreground = statusBrush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                Grid.SetColumn(statusBlock, 2);
                headerGrid.Children.Add(statusBlock);
            }

            grid.Children.Add(headerGrid);

            // Progress & Details Row (Row 1)
            var usageDetailGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            usageDetailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Progress Bar
            usageDetailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Values/Text

            // Progress Bar
            if ((usage.UsagePercentage > 0 || usage.IsQuotaBased) && !isMissing && !isError)
            {
                var pGrid = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                pGrid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)), CornerRadius = new CornerRadius(2) });

                var indicatorWidth = Math.Min(usage.UsagePercentage, 100);
                if (_preferences.InvertProgressBar) indicatorWidth = Math.Max(0, 100 - indicatorWidth);

                var fillGrid = new Grid();
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(indicatorWidth, GridUnitType.Star) });
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - indicatorWidth), GridUnitType.Star) });

                var fill = new Border
                {
                    Background = usage.UsagePercentage > _preferences.ColorThresholdRed ? Brushes.Crimson : (usage.UsagePercentage > _preferences.ColorThresholdYellow ? Brushes.Gold : Brushes.MediumSeaGreen),
                    CornerRadius = new CornerRadius(2)
                };
                fillGrid.Children.Add(fill);
                pGrid.Children.Add(fillGrid);

                usageDetailGrid.Children.Add(pGrid);
            }

            // Details Text (The tokens/credits/cost)
            var detailText = usage.Description;

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
                    Foreground = Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 200, // Ensure some bar space remains
                    ToolTip = detailText
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
                     Foreground = Brushes.Gold, 
                     Margin = new Thickness(22, 2, 0, 0),
                     FontWeight = FontWeights.SemiBold
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

            container.Child = grid;
            return container;
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshData(forceRefresh: true);
        }

        private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = ((App)Application.Current).Services.GetRequiredService<SettingsWindow>();
            settingsWindow.Owner = this;
            settingsWindow.Closed += async (s, args) => 
            {
                 if (this.IsVisible) await RefreshData(forceRefresh: true);
            };
            settingsWindow.Show();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Actually close the window (App will create new one next time)
        }
    }
}
