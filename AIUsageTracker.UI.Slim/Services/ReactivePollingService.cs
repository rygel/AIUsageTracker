// <copyright file="ReactivePollingService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Reactive polling service using System.Reactive for periodic data fetching.
/// </summary>
public class ReactivePollingService : IReactivePollingService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    private readonly IMonitorService _monitorService;
    private readonly ILogger<ReactivePollingService> _logger;
    private readonly Subject<IReadOnlyList<ProviderUsage>> _usageSubject;
    private readonly Subject<Exception> _errorSubject;
    private readonly BehaviorSubject<TimeSpan> _intervalSubject;
    private readonly CompositeDisposable _disposables;

    private IDisposable? _pollingSubscription;
    private bool _isPolling;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReactivePollingService"/> class.
    /// </summary>
    /// <param name="monitorService">The monitor service for fetching usage data.</param>
    /// <param name="logger">The logger.</param>
    public ReactivePollingService(
        IMonitorService monitorService,
        ILogger<ReactivePollingService> logger)
    {
        this._monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

        this._usageSubject = new Subject<IReadOnlyList<ProviderUsage>>();
        this._errorSubject = new Subject<Exception>();
        this._intervalSubject = new BehaviorSubject<TimeSpan>(DefaultInterval);
        this._disposables = new CompositeDisposable();

        // Add subjects to disposables for cleanup
        this._disposables.Add(this._usageSubject);
        this._disposables.Add(this._errorSubject);
        this._disposables.Add(this._intervalSubject);
    }

    /// <inheritdoc />
    public IObservable<IReadOnlyList<ProviderUsage>> UsageStream => this._usageSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<Exception> ErrorStream => this._errorSubject.AsObservable();

    /// <inheritdoc />
    public bool IsPolling => this._isPolling;

    /// <inheritdoc />
    public TimeSpan Interval
    {
        get => this._intervalSubject.Value;
        set
        {
            if (this._disposed)
            {
                return;
            }

            this._intervalSubject.OnNext(value);
            this._logger.LogDebug("Polling interval changed to {Interval}", value);

            // If currently polling, restart with new interval
            if (this._isPolling)
            {
                this.Stop();
                this.Start();
            }
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        if (this._disposed || this._isPolling)
        {
            return;
        }

        this._isPolling = true;
        this._logger.LogDebug("Starting reactive polling with interval {Interval}", this.Interval);

        // Create a dynamic interval observable that switches when interval changes
        this._pollingSubscription = this._intervalSubject
            .DistinctUntilChanged()
            .Select(interval => Observable.Interval(interval))
            .Switch()
            .SelectMany(_ => Observable.FromAsync(this.PollAsync))
            .Retry() // Keep polling even after errors
            .Subscribe(
                _ => { }, // Results are pushed through _usageSubject
                ex => this._logger.LogError(ex, "Fatal polling stream error"));

        // Trigger an immediate poll on start
        _ = this.RefreshNowAsync();
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!this._isPolling)
        {
            return;
        }

        this._pollingSubscription?.Dispose();
        this._pollingSubscription = null;
        this._isPolling = false;
        this._logger.LogDebug("Reactive polling stopped");
    }

    /// <inheritdoc />
    public async Task RefreshNowAsync()
    {
        if (this._disposed)
        {
            return;
        }

        await this.PollAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates an observable that emits usage data on each polling interval.
    /// This observable can be used directly for more advanced scenarios.
    /// </summary>
    /// <param name="interval">The polling interval.</param>
    /// <returns>An observable of usage data.</returns>
    public IObservable<IReadOnlyList<ProviderUsage>> CreatePollingObservable(TimeSpan interval)
    {
        return Observable
            .Timer(TimeSpan.Zero, interval)
            .SelectMany(_ => Observable.FromAsync(async ct =>
            {
                try
                {
                    return await this._monitorService.GetUsageAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "Polling error in observable");
                    throw;
                }
            }))
            .Catch<IReadOnlyList<ProviderUsage>, Exception>(ex =>
            {
                this._errorSubject.OnNext(ex);
                return Observable.Empty<IReadOnlyList<ProviderUsage>>();
            })
            .Repeat();
    }

    /// <summary>
    /// Disposes the service and releases all resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (this._disposed)
        {
            return;
        }

        if (disposing)
        {
            this.Stop();
            this._disposables.Dispose();
        }

        this._disposed = true;
    }

    private async Task<IReadOnlyList<ProviderUsage>> PollAsync()
    {
        this._logger.LogDebug("Starting poll");

        try
        {
            var usages = await this._monitorService.GetUsageAsync().ConfigureAwait(false);
            this._logger.LogDebug("Poll completed, received {Count} usages", usages.Count);

            this._usageSubject.OnNext(usages);
            return usages;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Polling failed");
            this._errorSubject.OnNext(ex);
            throw;
        }
    }
}
