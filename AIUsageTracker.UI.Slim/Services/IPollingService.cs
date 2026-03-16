// <copyright file="IPollingService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service interface for managing periodic polling of provider data.
/// </summary>
public interface IPollingService : IDisposable
{
    /// <summary>
    /// Fired when new usage data is available.
    /// </summary>
    event EventHandler<UsageUpdatedEventArgs>? UsageUpdated;

    /// <summary>
    /// Fired when a polling error occurs.
    /// </summary>
    event EventHandler<PollingErrorEventArgs>? PollingError;

    /// <summary>
    /// Gets a value indicating whether polling is currently active.
    /// </summary>
    bool IsPolling { get; }

    /// <summary>
    /// Gets the current polling interval.
    /// </summary>
    TimeSpan CurrentInterval { get; }

    /// <summary>
    /// Starts the polling timer.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the polling timer.
    /// </summary>
    void Stop();

    /// <summary>
    /// Performs an immediate refresh outside of the normal polling cycle.
    /// </summary>
    Task RefreshNowAsync();

    /// <summary>
    /// Sets the polling interval.
    /// </summary>
    /// <param name="interval">The new polling interval.</param>
    void SetInterval(TimeSpan interval);
}
