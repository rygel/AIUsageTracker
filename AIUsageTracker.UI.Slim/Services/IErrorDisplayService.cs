// <copyright file="IErrorDisplayService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service interface for displaying error messages and notifications to the user.
/// </summary>
public interface IErrorDisplayService
{
    /// <summary>
    /// Shows an error message dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The error message to display.</param>
    /// <param name="ex">Optional exception for logging.</param>
    void ShowError(string title, string message, Exception? ex = null);

    /// <summary>
    /// Shows a warning message dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The warning message to display.</param>
    void ShowWarning(string title, string message);

    /// <summary>
    /// Shows an informational message dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The informational message to display.</param>
    void ShowInfo(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog and returns the user's choice.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The confirmation message to display.</param>
    /// <returns>True if the user confirmed, false otherwise.</returns>
    Task<bool> ConfirmAsync(string title, string message);
}
