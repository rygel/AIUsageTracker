using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading.Tasks;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Interfaces;

namespace AIConsumptionTracker.UI
{
    public partial class InfoDialog : Window
    {
        private bool _isPrivacyMode = false;
        private string? _realUserName;
        private string? _realConfigPath;
        private readonly IConfigLoader? _configLoader;

        public InfoDialog()
        {
            InitializeComponent();
            
                // Get config loader from app services if available
                if (Application.Current is App app)
                {
                    _configLoader = (IConfigLoader?)app.Services.GetService(typeof(IConfigLoader));
                }
            
            LoadTheme();
            LoadInfo();
        }
        
        private async void LoadTheme()
        {
            try
            {
                AppPreferences? prefs = null;
                
                if (_configLoader != null)
                {
                    prefs = await _configLoader.LoadPreferencesAsync();
                }
                else if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
                {
                    // Try to get theme from main window's current state
                    var bg = mainWindow.Background as SolidColorBrush;
                    if (bg != null && bg.Color.R > 200) // Light theme detected
                    {
                        prefs = new AppPreferences { Theme = AppTheme.Light };
                    }
                }
                
                if (prefs != null)
                {
                    ApplyTheme(prefs.Theme);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARNING] Failed to load theme in InfoDialog: {ex.Message}");
            }
        }
        
        private void ApplyTheme(AppTheme theme)
        {
            var isDark = theme == AppTheme.Dark;
            var windowBg = isDark ? Color.FromRgb(30, 30, 30) : Color.FromRgb(243, 243, 243);
            var windowFg = isDark ? Brushes.White : Brushes.Black;
            var headerFooterBg = isDark ? Color.FromRgb(37, 37, 38) : Color.FromRgb(230, 230, 230);
            var borderColor = isDark ? Color.FromRgb(51, 51, 51) : Color.FromRgb(204, 204, 204);
            
            this.Background = new SolidColorBrush(windowBg);
            this.Foreground = windowFg;
            
            // Update header border
            if (this.FindName("HeaderBorder") is Border headerBorder)
            {
                headerBorder.Background = new SolidColorBrush(headerFooterBg);
            }
            
            // Update footer border
            if (this.FindName("FooterBorder") is Border footerBorder)
            {
                footerBorder.Background = new SolidColorBrush(headerFooterBg);
            }
            
            // Apply to all TextBlocks
            ApplyThemeToVisualTree(this, windowFg);
        }
        
        private void ApplyThemeToVisualTree(DependencyObject element, Brush foreground)
        {
            if (element == null) return;
            
            // Apply to TextBlock
            if (element is TextBlock textBlock)
            {
                textBlock.Foreground = foreground;
            }
            
            // Recurse through children
            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                ApplyThemeToVisualTree(child, foreground);
            }
        }

        public async Task PrepareForScreenshot(AppPreferences prefs)
        {
            _isPrivacyMode = true; 
            UpdatePrivacyUI();
            UpdateLayout();
            await Task.Yield();
        }

        private void LoadInfo()
        {
            // Subscribe to global privacy changes
            if (Application.Current is App app)
            {
                app.PrivacyChanged += (s, isPrivate) => {
                    _isPrivacyMode = isPrivate;
                    UpdatePrivacyUI();
                };
            }

            // Application version
            var appVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            if (appVersion != null)
            {
                InternalVersionText.Text = $"v{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}";
            }

            // .NET Runtime version
            DotNetVersionText.Text = RuntimeInformation.FrameworkDescription;

            // Operating System
            OsVersionText.Text = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

            // Architecture
            ArchitectureText.Text = RuntimeInformation.ProcessArchitecture.ToString();

            // Machine name
            MachineNameText.Text = Environment.MachineName;

            // Current user
            _realUserName = Environment.UserName;
            
            // Configuration File Path
            _realConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker", "auth.json");
            
            UpdatePrivacyUI();
        }

        private void UpdatePrivacyUI()
        {
            if (_isPrivacyMode)
            {
                UserNameText.Text = AIConsumptionTracker.Infrastructure.Helpers.PrivacyHelper.MaskString(_realUserName ?? "User");
                ConfigLinkText.Text = AIConsumptionTracker.Infrastructure.Helpers.PrivacyHelper.MaskPath(_realConfigPath ?? "Path");
                PrivacyBtn.Foreground = Brushes.Gold;
            }
            else
            {
                UserNameText.Text = _realUserName;
                ConfigLinkText.Text = _realConfigPath;
                PrivacyBtn.Foreground = Brushes.Gray;
            }
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
                _isPrivacyMode = !_isPrivacyMode;
                UpdatePrivacyUI();
                await Task.CompletedTask;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Header_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void ConfigPath_Click(object sender, RoutedEventArgs e)
        {
            var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker", "auth.json");
            if (File.Exists(configPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{configPath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var directory = Path.GetDirectoryName(configPath);
                if (directory != null && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{directory}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
    }
}
