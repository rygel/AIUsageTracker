using System;
using System.Reflection;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AIConsumptionTracker.UI
{
    public partial class InfoDialog : Window
    {
        public InfoDialog()
        {
            InitializeComponent();
            LoadInfo();
        }

        private void LoadInfo()
        {
            try
            {
                // Get .NET version
                string netVersion = GetNetCoreVersion();
                NetVersionText.Text = netVersion;

                // Get assembly version
                var assembly = Assembly.GetEntryAssembly();
                if (assembly != null)
                {
                    var version = assembly.GetName().Version;
                    if (version != null)
                    {
                        AppVersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
                    }

                    // Try to get build date from file version info
                    var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                    int buildNumber = fileVersionInfo.FileBuildPart;
                    
                    if (buildNumber > 0)
                    {
                        // Build number to date conversion (this is approximate)
                        // Build number is days since 1/1/2000
                        DateTime buildDate = new DateTime(2000, 1, 1).AddDays(buildNumber);
                        BuildDateText.Text = buildDate.ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        BuildDateText.Text = "Unknown";
                    }
                }
            }
            catch (Exception ex)
            {
                NetVersionText.Text = $"Error: {ex.Message}";
                AppVersionText.Text = "Error";
                BuildDateText.Text = "Error";
            }
        }

        private string GetNetCoreVersion()
        {
            // .NET version detection
            return RuntimeInformation.FrameworkDescription;
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}