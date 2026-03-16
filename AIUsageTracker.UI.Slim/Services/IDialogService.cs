// <copyright file="IDialogService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service interface for showing application dialogs.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the settings dialog.
    /// </summary>
    /// <param name="owner">Optional owner window.</param>
    /// <returns>True if changes were made, false otherwise, or null if canceled.</returns>
    Task<bool?> ShowSettingsAsync(Window? owner = null);

    /// <summary>
    /// Shows the info/about dialog.
    /// </summary>
    /// <param name="owner">Optional owner window.</param>
    Task ShowInfoAsync(Window? owner = null);

    /// <summary>
    /// Shows a save file dialog.
    /// </summary>
    /// <param name="filter">The file filter (e.g., "CSV Files|*.csv").</param>
    /// <param name="defaultFileName">The default file name.</param>
    /// <returns>The selected file path, or null if canceled.</returns>
    Task<string?> ShowSaveFileDialogAsync(string filter, string defaultFileName);

    /// <summary>
    /// Shows an open file dialog.
    /// </summary>
    /// <param name="filter">The file filter (e.g., "CSV Files|*.csv").</param>
    /// <returns>The selected file path, or null if canceled.</returns>
    Task<string?> ShowOpenFileDialogAsync(string filter);
}
