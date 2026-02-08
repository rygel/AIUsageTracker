using System;
using System.Reflection;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.UI
{
    public partial class InfoDialog : Window
    {
        private bool _isPrivacyMode = false;
        private string? _realUserName;
        private string? _realConfigPath;

        public InfoDialog()
        {
            InitializeComponent();
            LoadInfo();
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
