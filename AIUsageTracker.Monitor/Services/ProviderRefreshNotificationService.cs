// <copyright file="ProviderRefreshNotificationService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AIUsageTracker.Monitor.Services;

internal sealed class ProviderRefreshNotificationService
{
    private readonly UsageAlertsService _usageAlertsService;
    private readonly IHubContext<UsageHub>? _hubContext;

    public ProviderRefreshNotificationService(
        UsageAlertsService usageAlertsService,
        IHubContext<UsageHub>? hubContext = null)
    {
        this._usageAlertsService = usageAlertsService;
        this._hubContext = hubContext;
    }

    public async Task NotifyRefreshStartedAsync()
    {
        if (this._hubContext != null)
        {
            await this._hubContext.Clients.All.SendAsync("RefreshStarted").ConfigureAwait(false);
        }
    }

    public async Task NotifyUsageUpdatedAsync()
    {
        if (this._hubContext != null)
        {
            await this._hubContext.Clients.All.SendAsync("UsageUpdated").ConfigureAwait(false);
        }
    }

    public async Task ProcessUsageAlertsAsync(
        List<ProviderUsage> usages,
        AppPreferences preferences,
        List<ProviderConfig> configs)
    {
        await this._usageAlertsService.DetectResetEventsAsync(usages).ConfigureAwait(false);
        this._usageAlertsService.CheckUsageAlerts(usages, preferences, configs);
    }
}
