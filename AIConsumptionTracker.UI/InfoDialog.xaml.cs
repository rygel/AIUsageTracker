using System;
using System.Reflection;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;

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
    }
}
