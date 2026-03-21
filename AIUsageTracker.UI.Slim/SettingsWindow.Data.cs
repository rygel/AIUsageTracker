// <copyright file="SettingsWindow.Data.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow
{
    private async void ExportCsvBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this._monitorService.RefreshPortAsync();
            var csv = await this._monitorService.ExportDataAsync("csv");
            if (string.IsNullOrEmpty(csv))
            {
                MessageBox.Show(
                    "No data to export or Monitor is not running.",
                    "Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"usage_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            };

            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(dialog.FileName, csv);
                MessageBox.Show(
                    $"Exported to {dialog.FileName}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Export failed: {ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportJsonBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this._monitorService.RefreshPortAsync();
            var json = await this._monitorService.ExportDataAsync("json");
            if (string.Equals(json, "[]", StringComparison.Ordinal) || string.IsNullOrEmpty(json))
            {
                MessageBox.Show(
                    "No data to export or Monitor is not running.",
                    "Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"usage_export_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            };

            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(dialog.FileName, json);
                MessageBox.Show(
                    $"Exported to {dialog.FileName}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Export failed: {ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BackupDbBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Database files (*.db)|*.db",
                DefaultExt = ".db",
                FileName = $"usage_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            };

            if (dialog.ShowDialog() == true)
            {
                var dbPath = this._pathProvider.GetDatabasePath();

                if (File.Exists(dbPath))
                {
                    File.Copy(dbPath, dialog.FileName, true);
                    MessageBox.Show(
                        $"Backup saved to {dialog.FileName}",
                        "Backup Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Database file not found.",
                        "Backup Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Backup failed: {ex.Message}",
                "Backup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await this._monitorService.GetHistoryAsync(100);
            this.HistoryDataGrid.ItemsSource = history;
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to load history");
        }
    }

    private async void RefreshHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = await this._monitorService.GetHistoryAsync(100);
            this.HistoryDataGrid.ItemsSource = history;

            if (history.Count == 0)
            {
                MessageBox.Show(
                    "No history data available. The agent may not have collected any data yet.",
                    "No Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load history: {ex.Message}",
                "History Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        this.HistoryDataGrid.ItemsSource = null;
    }
}
