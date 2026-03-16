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
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class InfoDialog : Window, IWeakEventListener
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
        this.ConfigDirText.Text = @"C:\Users\***\...\AIUsageTracker";
        this.DataDirText.Text = @"C:\Users\***\...\AIUsageTracker";
        this.PrivacyBtn.Foreground = Brushes.Gold;
    }

    /// <inheritdoc />
    public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
    {
        if (managerType == typeof(PrivacyChangedWeakEventManager) && e is PrivacyChangedEventArgs args)
        {
            this._isPrivacyMode = args.IsPrivacyMode;
            this.UpdatePrivacyUI();
            return true;
        }

        return false;
    }

    private void LoadInfo()
    {
        // Subscribe to global privacy changes using WeakEventManager to prevent memory leaks
        if (Application.Current is App)
        {
            PrivacyChangedWeakEventManager.AddHandler(this.OnPrivacyChanged);

            // Set initial privacy state
            this._isPrivacyMode = App.IsPrivacyMode;
        }

        // Application version (include prerelease label like Beta/RC when present)
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var appVersion = assembly.GetName().Version;
        var versionCore = appVersion != null
            ? $"{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}"
            : "0.0.0";
        var suffix = GetPrereleaseLabel(assembly);
        this.InternalVersionText.Text = string.IsNullOrWhiteSpace(suffix)
            ? $"v{versionCore}"
            : $"v{versionCore} {suffix}";

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

        // Configuration Directory path (app-owned config location)
        this._realConfigDir = Path.GetDirectoryName(this._pathProvider.GetProviderConfigFilePath());

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

    private void OnPrivacyChanged(object? sender, PrivacyChangedEventArgs e)
    {
        this._isPrivacyMode = e.IsPrivacyMode;
        this.UpdatePrivacyUI();
    }

    private static string? GetPrereleaseLabel(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var normalized = informationalVersion.Split('+')[0];
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex < 0 || dashIndex >= normalized.Length - 1)
        {
            return null;
        }

        var suffix = normalized[(dashIndex + 1)..];
        if (suffix.StartsWith("beta.", StringComparison.OrdinalIgnoreCase))
        {
            var betaPart = suffix["beta.".Length..];
            return string.IsNullOrWhiteSpace(betaPart) ? "Beta" : $"Beta {betaPart}";
        }

        if (suffix.StartsWith("alpha.", StringComparison.OrdinalIgnoreCase))
        {
            var alphaPart = suffix["alpha.".Length..];
            return string.IsNullOrWhiteSpace(alphaPart) ? "Alpha" : $"Alpha {alphaPart}";
        }

        if (suffix.StartsWith("rc.", StringComparison.OrdinalIgnoreCase))
        {
            var rcPart = suffix["rc.".Length..];
            return string.IsNullOrWhiteSpace(rcPart) ? "RC" : $"RC {rcPart}";
        }

        return suffix.Replace('.', ' ');
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
