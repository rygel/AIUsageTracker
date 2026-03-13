// <copyright file="IReactivePollingService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Reactive polling service interface using Observable streams.
/// </summary>
public interface IReactivePollingService : IDisposable
{
    /// <summary>
    /// Gets an observable stream of usage data that emits on each polling interval.
    /// The stream automatically handles errors and continues polling.
    /// </summary>
    IObservable<IReadOnlyList<ProviderUsage>> UsageStream { get; }

    /// <summary>
    /// Gets an observable stream of polling errors.
    /// </summary>
    IObservable<Exception> ErrorStream { get; }

    /// <summary>
    /// Gets a value indicating whether polling is currently active.
    /// </summary>
    bool IsPolling { get; }

    /// <summary>
    /// Gets or sets the current polling interval.
    /// </summary>
    TimeSpan Interval { get; set; }

    /// <summary>
    /// Starts the polling stream.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the polling stream.
    /// </summary>
    void Stop();

    /// <summary>
    /// Triggers an immediate refresh, returning the result through the UsageStream.
    /// </summary>
    /// <returns>A task that completes when the refresh is done.</returns>
    Task RefreshNowAsync();
}
