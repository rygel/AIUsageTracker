using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AIConsumptionTracker.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AIConsumptionTracker.UI
{
    public partial class GitHubLoginDialog : Window
    {
        private readonly IGitHubAuthService _authService;
        private string _deviceCode = "";
        private string _verificationUri = "";
        private bool _isPolling = false;

        public bool IsAuthenticated { get; private set; } = false;

        public GitHubLoginDialog(IGitHubAuthService authService)
        {
            InitializeComponent();
            _authService = authService;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadingBar.Visibility = Visibility.Visible;
            StatusText.Text = "Connecting to GitHub...";
            CopyBtn.IsEnabled = false;

            try
            {
                var result = await _authService.InitiateDeviceFlowAsync();
                
                _deviceCode = result.deviceCode;
                _verificationUri = result.verificationUri;
                UserCodeText.Text = result.userCode;
                VerificationUrlText.Text = result.verificationUri;
                
                LoadingBar.Visibility = Visibility.Visible; // Keep spinning while polling
                StatusText.Text = "Waiting for authorization...";
                CopyBtn.IsEnabled = true;

                StartPolling(result.interval);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void StartPolling(int intervalSeconds)
        {
            _isPolling = true;
            int delay = intervalSeconds * 1000;
            
            // Safety timeout (e.g., 5 minutes)
            var timeout = DateTime.UtcNow.AddMinutes(15); 

            while (_isPolling && DateTime.UtcNow < timeout)
            {
                // Wait interval + small buffer
                await Task.Delay(delay + 500);
                if (!_isPolling) break;

                try
                {
                    var token = await _authService.PollForTokenAsync(_deviceCode, intervalSeconds);
                    if (token == "SLOW_DOWN")
                    {
                        delay += 5000; // Increase delay
                        continue;
                    }
                    
                    if (!string.IsNullOrEmpty(token))
                    {
                        StatusText.Text = "Authenticated successfully!";
                        StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                        LoadingBar.Visibility = Visibility.Collapsed;
                        IsAuthenticated = true;
                        
                        // Short delay for user to see success
                        await Task.Delay(1000);
                        DialogResult = true;
                        Close();
                        return;
                    }
                }
                catch (Exception ex)
                {
                   // Token expired or access denied
                   if (ex.Message.Contains("expired") || ex.Message.Contains("denied"))
                   {
                       StatusText.Text = "Authorization failed or expired.";
                       LoadingBar.Visibility = Visibility.Collapsed;
                       _isPolling = false;
                       return;
                   }
                }
            }
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(UserCodeText.Text);
                StatusText.Text = "Code copied! Please visit the link.";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                 StatusText.Text = "Copy failed. Please copy manually or type the code.";
            }
            catch (Exception)
            {
                 StatusText.Text = "Copy failed. Please copy manually.";
            }
            
            // Auto-open browser
            try {
                if (!string.IsNullOrEmpty(_verificationUri))
                    Process.Start(new ProcessStartInfo(_verificationUri) { UseShellExecute = true });
            } catch { }
        }

        private void Url_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try {
                Process.Start(new ProcessStartInfo(VerificationUrlText.Text) { UseShellExecute = true });
            } catch { }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPolling = false;
            DialogResult = false;
            Close();
        }

        private void AboutBtn_Click(object sender, RoutedEventArgs e)
        {
            var infoDialog = ((App)Application.Current).Services.GetRequiredService<InfoDialog>();
            infoDialog.Owner = this;
            infoDialog.ShowDialog();
        }

        protected override void OnClosed(EventArgs e)
        {
            _isPolling = false;
            base.OnClosed(e);
        }
    }
}
