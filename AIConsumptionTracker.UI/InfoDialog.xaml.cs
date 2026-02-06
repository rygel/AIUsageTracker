using System;
<<<<<<< HEAD
using System.Reflection;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
=======
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
>>>>>>> 4a3ff3e (Add InfoDialog with right-click menu integration)

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
<<<<<<< HEAD
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
=======
            // Application version
            var appVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            if (appVersion != null)
            {
                AppVersionText.Text = $"v{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}";
                VersionText.Text = AppVersionText.Text;
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
            UserNameText.Text = Environment.UserName;

            // Current directory
            CurrentDirText.Text = AppDomain.CurrentDomain.BaseDirectory;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
>>>>>>> 4a3ff3e (Add InfoDialog with right-click menu integration)
        {
            this.Close();
        }
    }
}