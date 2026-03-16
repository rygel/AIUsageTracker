// <copyright file="ErrorDisplayService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service for displaying error messages and notifications to the user.
/// </summary>
public class ErrorDisplayService : IErrorDisplayService
{
    private readonly ILogger<ErrorDisplayService> _logger;

    public ErrorDisplayService(ILogger<ErrorDisplayService> logger)
    {
        this._logger = logger;
    }

    /// <inheritdoc />
    public void ShowError(string title, string message, Exception? ex = null)
    {
        if (ex != null)
        {
            this._logger.LogError(ex, "Error displayed to user: {Title} - {Message}", title, message);
        }
        else
        {
            this._logger.LogError("Error displayed to user: {Title} - {Message}", title, message);
        }

        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <inheritdoc />
    public void ShowWarning(string title, string message)
    {
        this._logger.LogWarning("Warning displayed to user: {Title} - {Message}", title, message);
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <inheritdoc />
    public void ShowInfo(string title, string message)
    {
        this._logger.LogInformation("Info displayed to user: {Title} - {Message}", title, message);
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }
}
