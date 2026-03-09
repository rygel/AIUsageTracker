// <copyright file="InfoDialog.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class InfoDialog : Window
{
    private readonly ILogger<InfoDialog> _logger;
    private readonly IAppPathProvider _pathProvider;
    private bool _isPrivacyMode = false;
    private string? _realUserName;
    private string? _realConfigDir;
    private string? _realDataDir;

    public InfoDialog()
    {
        this.InitializeComponent();
        this._logger = App.CreateLogger<InfoDialog>();
        this._pathProvider = App.Host.Services.GetRequiredService<IAppPathProvider>();

        // In Slim UI, we rely on App.Preferences or direct theme resources
        // No need for complex theme loading or IConfigLoader here
        this.LoadInfo();
    }

    internal void PrepareForHeadlessScreenshot()
    {
        this._isPrivacyMode = true;

        this.InternalVersionText.Text = "v2.1.2";
        this.DotNetVersionText.Text = ".NET 8.0";
        this.OsVersionText.Text = "Windows 10 (x64)";
        this.ArchitectureText.Text = "X64";
        this.MachineNameText.Text = "WORKSTATION";
        this.UserNameText.Text = "d***r";
        this.ConfigDirText.Text = @"C:\Users\***\...\.opencode";
        this.DataDirText.Text = @"C:\Users\***\...\AIUsageTracker";
        this.PrivacyBtn.Foreground = Brushes.Gold;
    }

    private void LoadInfo()
    {
        // Subscribe to global privacy changes
        if (Application.Current is App)
        {
            App.PrivacyChanged += (_, e) =>
            {
                this._isPrivacyMode = e.IsPrivacyMode;
                this.UpdatePrivacyUI();
            };

            // Set initial privacy state
            this._isPrivacyMode = App.IsPrivacyMode;
        }

        // Application version
        var appVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        if (appVersion != null)
        {
            this.InternalVersionText.Text = $"v{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}";
        }

        // .NET Runtime version
        this.DotNetVersionText.Text = RuntimeInformation.FrameworkDescription;

        // Operating System
        this.OsVersionText.Text = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

        // Architecture
        this.ArchitectureText.Text = RuntimeInformation.ProcessArchitecture.ToString();

        // Machine name
        this.MachineNameText.Text = Environment.MachineName;

        // Current user
        this._realUserName = Environment.UserName;

        // Configuration Directory path (without auth.json)
        this._realConfigDir = Path.GetDirectoryName(this._pathProvider.GetAuthFilePath());

        // Data Directory path
        this._realDataDir = this._pathProvider.GetAppDataRoot();

        this.UpdatePrivacyUI();
    }

    private void UpdatePrivacyUI()
    {
        if (this._isPrivacyMode)
        {
            this.UserNameText.Text = this.MaskString(this._realUserName ?? "User");
            this.ConfigDirText.Text = this.MaskPath(this._realConfigDir ?? "Path");
            this.DataDirText.Text = this.MaskPath(this._realDataDir ?? "Path");
            this.PrivacyBtn.Foreground = Brushes.Gold;
        }
        else
        {
            this.UserNameText.Text = this._realUserName;
            this.ConfigDirText.Text = this._realConfigDir;
            this.DataDirText.Text = this._realDataDir;
            this.PrivacyBtn.Foreground = Brushes.Gray;
        }
    }

    // Helper methods for masking (since we don't reference Infrastructure directly in some Slim logic ideally)
    // Or we could duplicate the PrivacyHelper logic here to keep Slim independent
    private string MaskString(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (input.Length <= 2)
        {
            return "**";
        }

        return input.Substring(0, 1) + new string('*', Math.Min(input.Length - 2, 5)) + input.Substring(input.Length - 1);
    }

    private string MaskPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var filename = Path.GetFileName(path);
        return Path.Combine("C:\\Users\\***\\...", filename);
    }

    private async Task PrivacyBtn_ClickAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            this._isPrivacyMode = !this._isPrivacyMode;
            App.SetPrivacyMode(this._isPrivacyMode);

            // App.PrivacyChanged event will handle UI update
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "PrivacyBtn_ClickAsync failed");
        }
    }

#pragma warning disable VSTHRD100 // XAML click handlers must be async void wrappers.
    private async void PrivacyBtn_Click(object sender, RoutedEventArgs e) => await this.PrivacyBtn_ClickAsync(sender, e);
#pragma warning restore VSTHRD100

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
        if (Directory.Exists(this._realConfigDir))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{this._realConfigDir}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to open config directory");
            }
        }
    }

    private void DataDir_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(this._realDataDir))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{this._realDataDir}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to open data directory");
            }
        }
    }
}
