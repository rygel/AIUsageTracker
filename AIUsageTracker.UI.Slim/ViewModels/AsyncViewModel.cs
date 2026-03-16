// <copyright file="AsyncViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using CommunityToolkit.Mvvm.ComponentModel;

namespace AIUsageTracker.UI.Slim.ViewModels;

/// <summary>
/// Base class for ViewModels that perform asynchronous operations with loading state management.
/// </summary>
public abstract partial class AsyncViewModel : BaseViewModel
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Executes an async action with loading state management.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <param name="busyMessage">The message to display while busy.</param>
    /// <returns>A task representing the operation.</returns>
    protected async Task ExecuteAsync(Func<Task> action, string busyMessage = "Loading...")
    {
        if (this.IsBusy)
        {
            return;
        }

        this.IsBusy = true;
        this.BusyMessage = busyMessage;
        this.HasError = false;
        this.ErrorMessage = string.Empty;

        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            this.HasError = true;
            this.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            this.IsBusy = false;
            this.BusyMessage = string.Empty;
        }
    }

    /// <summary>
    /// Executes an async action with loading state management, returning a result.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="action">The async action to execute.</param>
    /// <param name="busyMessage">The message to display while busy.</param>
    /// <returns>The result of the action.</returns>
    protected async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string busyMessage = "Loading...")
    {
        if (this.IsBusy)
        {
            return default!;
        }

        this.IsBusy = true;
        this.BusyMessage = busyMessage;
        this.HasError = false;
        this.ErrorMessage = string.Empty;

        try
        {
            return await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            this.HasError = true;
            this.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            this.IsBusy = false;
            this.BusyMessage = string.Empty;
        }
    }

    /// <summary>
    /// Clears any error state.
    /// </summary>
    protected void ClearError()
    {
        this.HasError = false;
        this.ErrorMessage = string.Empty;
    }
}
