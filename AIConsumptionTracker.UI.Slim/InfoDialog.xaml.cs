using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading.Tasks;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.UI.Slim
{
    public partial class InfoDialog : Window
    {
        private bool _isPrivacyMode = false;
        private string? _realUserName;
        private string? _realConfigDir;
        private string? _realDataDir;

        public InfoDialog()
        {
            InitializeComponent();
            
            // In Slim UI, we rely on App.Preferences or direct theme resources
            // No need for complex theme loading or IConfigLoader here
            
            LoadInfo();
        }
        
        private void LoadInfo()
        {
            // Subscribe to global privacy changes
            if (Application.Current is App)
            {
                App.PrivacyChanged += (s, isPrivate) => {
                    _isPrivacyMode = isPrivate;
                    UpdatePrivacyUI();
                };
                
                // Set initial privacy state
                _isPrivacyMode = App.IsPrivacyMode;
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
                UserNameText.Text = MaskString(_realUserName ?? "User");
                ConfigDirText.Text = MaskPath(_realConfigDir ?? "Path");
                DataDirText.Text = MaskPath(_realDataDir ?? "Path");
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
        
        // Helper methods for masking (since we don't reference Infrastructure directly in some Slim logic ideally)
        // Or we could duplicate the PrivacyHelper logic here to keep Slim independent
        private string MaskString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (input.Length <= 2) return "**";
            return input.Substring(0, 1) + new string('*', Math.Min(input.Length - 2, 5)) + input.Substring(input.Length - 1);
        }

        private string MaskPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var filename = Path.GetFileName(path);
            return Path.Combine("C:\\Users\\***\\...", filename);
        }

        internal void PrepareForHeadlessScreenshot()
        {
            _isPrivacyMode = true;

            InternalVersionText.Text = "v2.1.2";
            DotNetVersionText.Text = ".NET 8.0";
            OsVersionText.Text = "Windows 10 (x64)";
            ArchitectureText.Text = "X64";
            MachineNameText.Text = "WORKSTATION";
            UserNameText.Text = "d***r";
            ConfigDirText.Text = @"C:\Users\***\...\ai-consumption-tracker";
            DataDirText.Text = @"C:\Users\***\...\AIConsumptionTracker\Agent";
            PrivacyBtn.Foreground = Brushes.Gold;
        }

        private async void PrivacyBtn_Click(object sender, RoutedEventArgs e) => await PrivacyBtn_ClickAsync(sender, e);

        internal async Task PrivacyBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            _isPrivacyMode = !_isPrivacyMode;
            App.SetPrivacyMode(_isPrivacyMode); 
            // App.PrivacyChanged event will handle UI update
            await Task.CompletedTask;
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
                try 
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{_realConfigDir}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open config dir: {ex.Message}");
                }
            }
        }

        private void DataDir_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_realDataDir))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{_realDataDir}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open data dir: {ex.Message}");
                }
            }
        }

    }
}
