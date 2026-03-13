// <copyright file="DialogService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service for showing application dialogs.
/// </summary>
public class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;

    public DialogService(IServiceProvider serviceProvider)
    {
        this._serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public Task<bool?> ShowSettingsAsync(Window? owner = null)
    {
        var settingsWindow = this._serviceProvider.GetRequiredService<SettingsWindow>();

        if (owner != null)
        {
            settingsWindow.Owner = owner;
        }

        var result = settingsWindow.ShowDialog();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task ShowInfoAsync(Window? owner = null)
    {
        var infoDialog = new InfoDialog();

        if (owner != null)
        {
            infoDialog.Owner = owner;
        }

        infoDialog.ShowDialog();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> ShowSaveFileDialogAsync(string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName,
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FileName : null);
    }

    /// <inheritdoc />
    public Task<string?> ShowOpenFileDialogAsync(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FileName : null);
    }
}
