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
        IList<ProviderUsage> usages,
        AppPreferences preferences,
        IList<ProviderConfig> configs)
    {
        var usagesList = usages as IReadOnlyList<ProviderUsage> ?? usages.ToList();
        var configsList = configs as IReadOnlyList<ProviderConfig> ?? configs.ToList();
        await this._usageAlertsService.DetectResetEventsAsync(usagesList).ConfigureAwait(false);
        this._usageAlertsService.CheckUsageAlerts(usagesList, preferences, configsList);
    }
}
