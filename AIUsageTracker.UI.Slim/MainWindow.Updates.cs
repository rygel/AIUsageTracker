// <copyright file="MainWindow.Updates.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class MainWindow : Window
{
    private void ViewChangelogBtn_Click(object sender, RoutedEventArgs e)
    {
        if (this._latestUpdate == null)
        {
            OpenExternalUrl(AIUsageTracker.Infrastructure.Services.GitHubUpdateChecker.GetReleasesPageUrl());
            return;
        }

        this.ShowChangelogWindow(this._latestUpdate);
    }

    private void ShowChangelogWindow(UpdateInfo updateInfo)
    {
        var changelogWindow = new Window
        {
            Title = $"Changelog - Version {updateInfo.Version}",
            Width = 680,
            Height = 520,
            MinWidth = 480,
            MinHeight = 320,
            Owner = this,
            Topmost = this.Topmost,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = GetResourceBrush("CardBackground", Brushes.Black),
            Foreground = GetResourceBrush("PrimaryText", Brushes.White),
        };

        var viewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            Document = this._buildChangelogDocument(updateInfo.ReleaseNotes),
        };

        changelogWindow.Content = viewer;

        this._isChangelogOpen = true;
        try
        {
            changelogWindow.ShowDialog();
        }
        finally
        {
            this._isChangelogOpen = false;
            this.EnsureAlwaysOnTop();
        }
    }

    private static void OpenExternalUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private async Task CheckForUpdatesAsync()
    {
        if (this._isUpdateCheckInProgress)
        {
            this._logger.LogDebug("Skipping overlapping update check request.");
            return;
        }

        try
        {
            this._isUpdateCheckInProgress = true;
            UiDiagnosticFileLog.Write("[UPDATE] Checking for updates...");
            this._latestUpdate = await this._updateChecker.CheckForUpdatesAsync().ConfigureAwait(true);

            var latestVersion = this._latestUpdate?.Version;
            if (!string.IsNullOrWhiteSpace(latestVersion))
            {
                UiDiagnosticFileLog.Write($"[UPDATE] New version available: {latestVersion} (download: {this._latestUpdate?.DownloadUrl})");
                if (this.UpdateNotificationBanner != null && this.UpdateText != null)
                {
                    this.UpdateText.Text = $"New version available: {latestVersion}";
                    this.UpdateNotificationBanner.Visibility = Visibility.Visible;
                }
            }
            else if (this.UpdateNotificationBanner != null)
            {
                UiDiagnosticFileLog.Write("[UPDATE] No updates available.");
                this.UpdateNotificationBanner.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UiDiagnosticFileLog.Write($"[UPDATE] Check failed: {ex.Message}");
            this._logger.LogWarning(ex, "Update check failed");
        }
        finally
        {
            this._isUpdateCheckInProgress = false;
        }
    }

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (this._latestUpdate == null)
        {
            OpenExternalUrl(AIUsageTracker.Infrastructure.Services.GitHubUpdateChecker.GetLatestReleasePageUrl());
            return;
        }

        await Services.UpdateInstallerHelper.DownloadAndInstallAsync(
            this,
            this._latestUpdate,
            this._updateChecker,
            this._logger,
            this.Topmost).ConfigureAwait(true);
    }
}
