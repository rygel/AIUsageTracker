// <copyright file="ProviderRefreshNotificationService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderRefreshNotificationService
{
    private readonly UsageAlertsService _usageAlertsService;
    private readonly IHubContext<UsageHub>? _hubContext;
    private string? _lastUsageHash;

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

    public async Task NotifyUsageUpdatedAsync(IReadOnlyList<ProviderUsage>? usages = null)
    {
        if (this._hubContext == null)
        {
            return;
        }

        if (usages != null)
        {
            var hash = ComputeUsageHash(usages);
            if (string.Equals(hash, this._lastUsageHash, StringComparison.Ordinal))
            {
                return;
            }

            this._lastUsageHash = hash;
        }

        await this._hubContext.Clients.All.SendAsync("UsageUpdated").ConfigureAwait(false);
    }

    internal static string ComputeUsageHash(IReadOnlyList<ProviderUsage> usages)
    {
        var sb = new StringBuilder();
        foreach (var usage in usages.OrderBy(u => u.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(usage.ProviderId)
                .Append('|')
                .Append(usage.UsedPercent.ToString("F1", CultureInfo.InvariantCulture))
                .Append('|')
                .Append(usage.IsAvailable ? '1' : '0')
                .Append('|')
                .Append(usage.HttpStatus.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(usage.Description ?? string.Empty)
                .Append('\n');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
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
