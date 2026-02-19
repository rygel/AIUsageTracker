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
        private string? _realConfigDir;
        private string? _realDataDir;
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
            SwitchTheme(isDark);
        }

        private void SwitchTheme(bool isDark)
        {
            try
            {
                var appResources = Application.Current.Resources;
                
                // Map resource keys - the theme files define Dark/Light prefixed keys
                // We need to swap the non-prefixed keys to point to the right theme
                var prefix = isDark ? "Dark" : "Light";
                
                // List of all resource keys to swap
                var resourceKeys = new[]
                {
                    "Background", "HeaderBackground", "FooterBackground", "BorderColor",
                    "ControlBackground", "ControlBorder", "InputBackground",
                    "PrimaryText", "SecondaryText", "TertiaryText", "AccentColor",
                    "ButtonBackground", "ButtonHover", "ButtonPressed", "ButtonForeground",
                    "TabUnselected", "ComboBoxBackground", "ComboBoxItemHover",
                    "CheckBoxForeground", "CardBackground", "CardBorder",
                    "GroupHeaderBackground", "GroupHeaderBorder",
                    "ScrollBarBackground", "ScrollBarForeground",
                    "LinkForeground", "UpdateBannerBackground", "UpdateButtonBackground",
                    "ProgressBarBackground", "ProgressBarGreen", "ProgressBarYellow", "ProgressBarRed",
                    "StatusTextNormal", "StatusTextMissing", "StatusTextError", "StatusTextConsole"
                };
                
                foreach (var key in resourceKeys)
                {
                    var themeKey = $"{prefix}{key}";
                    if (appResources.Contains(themeKey))
                    {
                        appResources[key] = appResources[themeKey];
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARNING] Failed to switch theme: {ex.Message}");
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
            
            // Configuration Directory path (without auth.json)
            _realConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker");
            
            // Data Directory path
            _realDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIConsumptionTracker", "Agent");
            
            UpdatePrivacyUI();
        }

        private void UpdatePrivacyUI()
        {
            if (_isPrivacyMode)
            {
                UserNameText.Text = AIConsumptionTracker.Infrastructure.Helpers.PrivacyHelper.MaskString(_realUserName ?? "User");
                ConfigDirText.Text = AIConsumptionTracker.Infrastructure.Helpers.PrivacyHelper.MaskPath(_realConfigDir ?? "Path");
                DataDirText.Text = AIConsumptionTracker.Infrastructure.Helpers.PrivacyHelper.MaskPath(_realDataDir ?? "Path");
                PrivacyBtn.Foreground = Brushes.Gold;
            }
            else
            {
                UserNameText.Text = _realUserName;
                ConfigDirText.Text = _realConfigDir;
                DataDirText.Text = _realDataDir;
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

        private void ConfigDir_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_realConfigDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_realConfigDir}\"",
                    UseShellExecute = true
                });
            }
        }

        private void DataDir_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_realDataDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_realDataDir}\"",
                    UseShellExecute = true
                });
            }
        }
    }
}
