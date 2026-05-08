using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

internal static class UpdateInstallerHelper
{
    public static async Task DownloadAndInstallAsync(
        Window owner,
        UpdateInfo updateInfo,
        GitHubUpdateChecker updateChecker,
        ILogger logger,
        bool topmost = false)
    {
        var confirmResult = MessageBox.Show(
            owner,
            $"Download and install version {updateInfo.Version}?\n\nThe application will restart after installation.",
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
                Owner = owner,
                Topmost = topmost,
                ResizeMode = ResizeMode.NoResize,
                Background = UIHelper.GetResourceBrush("Background", Brushes.Black),
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Downloading version {updateInfo.Version}...",
                            Margin = new Thickness(0, 0, 0, 10),
                            Foreground = UIHelper.GetResourceBrush("PrimaryText", Brushes.White),
                        },
                        progressBar,
                    },
                },
            };

            var progress = new Progress<double>(p => progressBar.Value = p);
            progressWindow.Show();

            UiDiagnosticFileLog.Write($"[UPDATE] Starting download: {updateInfo.DownloadUrl}");
            var result = await updateChecker.DownloadAndInstallUpdateAsync(updateInfo, progress).ConfigureAwait(true);
            progressWindow.Close();
            progressWindow = null;

            UiDiagnosticFileLog.WriteUpdateAttemptSummary(
                result.AttemptId,
                updateInfo.Version,
                updateInfo.DownloadUrl ?? string.Empty,
                result.Success,
                result.FailureReason,
                result.InstallerPath,
                result.InstallerSha256);

            if (result.Success)
            {
                logger.LogInformation("[UPDATE] Download and install succeeded, shutting down.");
                UiDiagnosticFileLog.Write("[UPDATE] Download and install succeeded, shutting down.");
                Application.Current.Shutdown();
            }
            else
            {
                logger.LogError("[UPDATE] Failed: {FailureReason}. Download URL: {DownloadUrl}", result.FailureReason, updateInfo.DownloadUrl);
                UiDiagnosticFileLog.Write($"[UPDATE] Failed: {result.FailureReason}");
                MessageBox.Show(
                    owner,
                    $"Failed to download or install version {updateInfo.Version}.\n\n" +
                    $"Reason: {result.FailureReason}\n\n" +
                    $"Download URL: {updateInfo.DownloadUrl}\n\n" +
                    "Please try again or download manually from the releases page.",
                    "Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progressWindow?.Close();
            logger.LogWarning(ex, "[UPDATE] Exception during download");
            UiDiagnosticFileLog.Write($"[UPDATE] Exception: {ex}");
            MessageBox.Show(
                owner,
                $"Update error: {ex.Message}",
                "Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
