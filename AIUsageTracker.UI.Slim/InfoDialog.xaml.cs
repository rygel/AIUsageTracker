// <copyright file="InfoDialog.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class InfoDialog : Window
{
    private readonly ILogger<InfoDialog> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly EventHandler<PrivacyChangedEventArgs> _privacyChangedHandler;
    private bool _isPrivacyMode = false;
    private string? _realUserName;
    private string? _realConfigDir;
    private string? _realDataDir;

    public InfoDialog(ILogger<InfoDialog> logger, IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(pathProvider);

        this.InitializeComponent();
        this._logger = logger;
        this._pathProvider = pathProvider;
        this._privacyChangedHandler = this.OnPrivacyChanged;

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
        this.DatabaseSizeText.Text = "12.3 MB";
        this.PrivacyBtn.Foreground = Brushes.Gold;
    }

    private void LoadInfo()
    {
        // Subscribe to global privacy changes using WeakEventManager to prevent memory leaks
        if (Application.Current is App)
        {
            PrivacyChangedWeakEventManager.AddHandler(this._privacyChangedHandler);

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

        // Database file size
        var dbPath = this._pathProvider.GetDatabasePath();
        try
        {
            var dbInfo = new FileInfo(dbPath);
            this.DatabaseSizeText.Text = dbInfo.Exists
                ? FormatFileSize(dbInfo.Length)
                : "not found";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            this._logger.LogWarning(ex, "Could not read database file size");
            this.DatabaseSizeText.Text = "unavailable";
        }

        this.UpdatePrivacyUI();
    }

    private void UpdatePrivacyUI()
    {
        if (this._isPrivacyMode)
        {
            this.UserNameText.Text = PrivacyHelper.MaskString(this._realUserName ?? "User");
            this.ConfigDirText.Text = PrivacyHelper.MaskPath(this._realConfigDir ?? "Path");
            this.DataDirText.Text = PrivacyHelper.MaskPath(this._realDataDir ?? "Path");
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

    private void OnPrivacyChanged(object? sender, PrivacyChangedEventArgs e)
    {
        this._isPrivacyMode = e.IsPrivacyMode;
        this.UpdatePrivacyUI();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):F1} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes} B";
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
        var dashIndex = normalized.IndexOf("-", StringComparison.Ordinal);
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

    private async Task PrivacyBtn_ClickAsync()
    {
        try
        {
            this._isPrivacyMode = !this._isPrivacyMode;
            App.SetPrivacyMode(this._isPrivacyMode);

            // Persist privacy preference regardless of which window toggled it.
            var preferencesStore = App.Host.Services.GetRequiredService<UiPreferencesStore>();
            var saved = await preferencesStore.SaveAsync(App.Preferences).ConfigureAwait(true);
            if (!saved)
            {
                this._logger.LogWarning("Failed to persist privacy mode from Info dialog.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(ex, "PrivacyBtn_ClickAsync failed");
        }
    }

#pragma warning disable VSTHRD100 // XAML click handlers must be async void wrappers.
    private async void PrivacyBtn_Click(object sender, RoutedEventArgs e) => await this.PrivacyBtn_ClickAsync().ConfigureAwait(true);
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
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
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
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                this._logger.LogWarning(ex, "Failed to open data directory");
            }
        }
    }
}
