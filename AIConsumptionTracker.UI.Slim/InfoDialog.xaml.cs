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
        private string? _realConfigPath;

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
            
            // Configuration File Path
            _realConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker", "auth.json");
            
            UpdatePrivacyUI();
        }

        private void UpdatePrivacyUI()
        {
            if (_isPrivacyMode)
            {
                UserNameText.Text = MaskString(_realUserName ?? "User");
                ConfigLinkText.Text = MaskPath(_realConfigPath ?? "Path");
                PrivacyBtn.Foreground = Brushes.Gold;
            }
            else
            {
                UserNameText.Text = _realUserName;
                ConfigLinkText.Text = _realConfigPath;
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

        private void ConfigPath_Click(object sender, RoutedEventArgs e)
        {
            var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker", "auth.json");
            if (File.Exists(configPath))
            {
                try 
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{configPath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open config: {ex.Message}");
                }
            }
            else
            {
                var directory = Path.GetDirectoryName(configPath);
                if (directory != null && Directory.Exists(directory))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{directory}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                         Debug.WriteLine($"Failed to open directory: {ex.Message}");
                    }
                }
            }
        }
    }
}
