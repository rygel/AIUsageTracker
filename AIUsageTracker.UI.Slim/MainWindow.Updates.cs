// <copyright file="MainWindow.Updates.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AIUsageTracker.Core.Interfaces;
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
            this._latestUpdate = await this._updateChecker.CheckForUpdatesAsync();

            var latestVersion = this._latestUpdate?.Version;
            if (!string.IsNullOrWhiteSpace(latestVersion))
            {
                if (this.UpdateNotificationBanner != null && this.UpdateText != null)
                {
                    this.UpdateText.Text = $"New version available: {latestVersion}";
                    this.UpdateNotificationBanner.Visibility = Visibility.Visible;
                }
            }
            else if (this.UpdateNotificationBanner != null)
            {
                this.UpdateNotificationBanner.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
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

        var result = MessageBox.Show(
            $"Download and install version {this._latestUpdate.Version}?\n\nThe application will restart after installation.",
            "Confirm Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
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
                ResizeMode = ResizeMode.NoResize,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = $"Downloading version {this._latestUpdate.Version}...", Margin = new Thickness(0, 0, 0, 10) },
                        progressBar,
                    },
                },
            };

            var progress = new Progress<double>(p => progressBar.Value = p);
            progressWindow.Show();

            var success = await this._updateChecker.DownloadAndInstallUpdateAsync(this._latestUpdate, progress);
            progressWindow.Close();
            progressWindow = null;

            if (success)
            {
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show(
                    "Failed to download or install the update. Please try again or download manually from the releases page.",
                    "Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            progressWindow?.Close();
            MessageBox.Show(
                $"Update error: {ex.Message}",
                "Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
