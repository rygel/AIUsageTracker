// <copyright file="PowerStateListener.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public sealed class PowerStateListener : IHostedService, IDisposable
{
    private readonly ILogger<PowerStateListener> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly Action _onSuspend;
    private readonly Action _onResume;
    private bool _subscribedToPowerEvents;
    private bool _disposed;

    public PowerStateListener(
        ILogger<PowerStateListener> logger,
        MonitorJobScheduler scheduler,
        IAppPathProvider pathProvider,
        Action? onSuspend = null,
        Action? onResume = null)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;

        this._onSuspend = onSuspend ?? (() =>
        {
            scheduler.Pause();
        });

        this._onResume = onResume ?? (() =>
        {
            scheduler.Resume();
            scheduler.Enqueue("post-resume-refresh", _ => Task.CompletedTask, MonitorJobPriority.High);
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            this.SubscribePowerEvents();
        }

        this._logger.LogInformation("Power state listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows() && this._subscribedToPowerEvents)
        {
            this.UnsubscribePowerEvents();
        }

        return Task.CompletedTask;
    }

    public void SimulateSuspend()
    {
        this.HandleSuspend();
    }

    public void SimulateResume()
    {
        this.HandleResume();
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;

        if (OperatingSystem.IsWindows() && this._subscribedToPowerEvents)
        {
            this.UnsubscribePowerEvents();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void SubscribePowerEvents()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged += this.OnPowerModeChanged;
        this._subscribedToPowerEvents = true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void UnsubscribePowerEvents()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= this.OnPowerModeChanged;
        this._subscribedToPowerEvents = false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case Microsoft.Win32.PowerModes.Suspend:
                this.HandleSuspend();
                break;
            case Microsoft.Win32.PowerModes.Resume:
                this.HandleResume();
                break;
        }
    }

    private void HandleSuspend()
    {
        this._logger.LogInformation("System suspend detected — pausing scheduler");
        this._onSuspend();
        MonitorInfoPersistence.SaveMonitorInfo(0, false, this._logger, this._pathProvider, startupStatus: "suspended");
    }

    private void HandleResume()
    {
        this._logger.LogInformation("System resume detected — resuming scheduler");
        this._onResume();
        MonitorInfoPersistence.SaveMonitorInfo(0, false, this._logger, this._pathProvider, startupStatus: "running");
    }
}
