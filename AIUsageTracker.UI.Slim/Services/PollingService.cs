// <copyright file="PollingService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Threading;
using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service for managing periodic polling of provider data.
/// </summary>
public class PollingService : IPollingService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    private readonly IMonitorService _monitorService;
    private readonly ILogger<PollingService> _logger;
    private readonly DispatcherTimer _timer;
    private readonly object _lockObject = new();

    private bool _isPollingInProgress;
    private bool _disposed;

    public PollingService(IMonitorService monitorService, ILogger<PollingService> logger)
    {
        this._monitorService = monitorService;
        this._logger = logger;
        this._timer = new DispatcherTimer
        {
            Interval = DefaultInterval,
        };
        this._timer.Tick += this.OnTimerTick;
    }

    /// <inheritdoc />
    public event EventHandler<UsageUpdatedEventArgs>? UsageUpdated;

    /// <inheritdoc />
    public event EventHandler<PollingErrorEventArgs>? PollingError;

    /// <inheritdoc />
    public bool IsPolling => this._timer.IsEnabled;

    /// <inheritdoc />
    public TimeSpan CurrentInterval => this._timer.Interval;

    /// <inheritdoc />
    public void Start()
    {
        if (this._disposed)
        {
            return;
        }

        this._timer.Start();
        this._logger.LogDebug("Polling started with interval {Interval}", this.CurrentInterval);
    }

    /// <inheritdoc />
    public void Stop()
    {
        this._timer.Stop();
        this._logger.LogDebug("Polling stopped");
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
    public void SetInterval(TimeSpan interval)
    {
        if (this._disposed)
        {
            return;
        }

        this._timer.Interval = interval;
        this._logger.LogDebug("Polling interval changed to {Interval}", interval);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (this._disposed)
        {
            return;
        }

        if (disposing)
        {
            this._timer.Stop();
            this._timer.Tick -= this.OnTimerTick;
        }

        this._disposed = true;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await this.PollAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Polling tick failed");
        }
    }

    private async Task PollAsync()
    {
        lock (this._lockObject)
        {
            if (this._isPollingInProgress)
            {
                this._logger.LogDebug("Polling already in progress, skipping");
                return;
            }

            this._isPollingInProgress = true;
        }

        try
        {
            this._logger.LogDebug("Starting poll");
            var usages = await this._monitorService.GetUsageAsync().ConfigureAwait(false);
            this._logger.LogDebug("Poll completed, received {Count} usages", usages.Count);

            UsageUpdated?.Invoke(this, new UsageUpdatedEventArgs(usages));
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Polling failed");
            PollingError?.Invoke(this, new PollingErrorEventArgs(ex, ex.Message));
        }
        finally
        {
            lock (this._lockObject)
            {
                this._isPollingInProgress = false;
            }
        }
    }
}
