// <copyright file="MainWindow.Updates.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
            Background = this.GetResourceBrush("CardBackground", Brushes.Black),
            Foreground = this.GetResourceBrush("PrimaryText", Brushes.White),
        };

        var viewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            Document = this._buildChangelogDocument(updateInfo.ReleaseNotes),
        };

        changelogWindow.Content = viewer;
        changelogWindow.ShowDialog();
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
            this._latestUpdate = await this._updateChecker.CheckForUpdatesAsync();

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
        catch (Exception ex)
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

        var confirmResult = MessageBox.Show(
            $"Download and install version {this._latestUpdate.Version}?\n\nThe application will restart after installation.",
            "Confirm Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmResult != MessageBoxResult.Yes)
        {
            return;
        }

        Window? progressWindow = null;

        try
        {
            var progressBar = new ProgressBar
            {
                Height = 20,
                Minimum = 0,
                Maximum = 100,
            };

            progressWindow = new Window
            {
                Title = "Downloading Update",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Topmost = this.Topmost,
                ResizeMode = ResizeMode.NoResize,
                Background = this.GetResourceBrush("Background", Brushes.Black),
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Downloading version {this._latestUpdate.Version}...",
                            Margin = new Thickness(0, 0, 0, 10),
                            Foreground = this.GetResourceBrush("PrimaryText", Brushes.White),
                        },
                        progressBar,
                    },
                },
            };

            var progress = new Progress<double>(p => progressBar.Value = p);
            progressWindow.Show();

            UiDiagnosticFileLog.Write($"[UPDATE] Starting download: {this._latestUpdate.DownloadUrl}");
            var result = await this._updateChecker.DownloadAndInstallUpdateAsync(this._latestUpdate, progress);
            progressWindow.Close();
            progressWindow = null;

            if (result.Success)
            {
                UiDiagnosticFileLog.Write("[UPDATE] Download and install succeeded, shutting down.");
                Application.Current.Shutdown();
            }
            else
            {
                UiDiagnosticFileLog.Write($"[UPDATE] Failed: {result.FailureReason}");
                MessageBox.Show(
                    $"Failed to download or install version {this._latestUpdate.Version}.\n\n" +
                    $"Reason: {result.FailureReason}\n\n" +
                    $"Download URL: {this._latestUpdate.DownloadUrl}\n\n" +
                    "Please try again or download manually from the releases page.",
                    "Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            progressWindow?.Close();
            UiDiagnosticFileLog.Write($"[UPDATE] Exception: {ex}");
            MessageBox.Show(
                $"Update error: {ex.Message}",
                "Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
