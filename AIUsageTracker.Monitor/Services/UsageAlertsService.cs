// <copyright file="UsageAlertsService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public class UsageAlertsService
{
    private readonly ILogger<UsageAlertsService> _logger;
    private readonly IUsageDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly IConfigService _configService;

    public UsageAlertsService(
        ILogger<UsageAlertsService> logger,
        IUsageDatabase database,
        INotificationService notificationService,
        IConfigService configService)
    {
        this._logger = logger;
        this._database = database;
        this._notificationService = notificationService;
        this._configService = configService;
    }

    public void CheckUsageAlerts(IReadOnlyList<ProviderUsage> usages, AppPreferences prefs, IReadOnlyList<ProviderConfig> configs)
    {
        if (!prefs.EnableNotifications || !prefs.NotifyOnUsageThreshold || IsInQuietHours(prefs))
        {
            return;
        }

        foreach (var usage in usages)
        {
            var config = configs.FirstOrDefault(c => c.ProviderId.Equals(usage.ProviderId, StringComparison.OrdinalIgnoreCase));
            var usedPercentage = UsageMath.GetEffectiveUsedPercent(usage);
            if (config != null && config.EnableNotifications && usedPercentage >= prefs.NotificationThreshold)
            {
                this._notificationService.ShowUsageAlert(usage.ProviderName, usedPercentage);
            }
        }
    }

    public async Task DetectResetEventsAsync(IReadOnlyList<ProviderUsage> currentUsages)
    {
        this._logger.LogDebug("Checking for reset events...");

        var allHistory = await this._database.GetRecentHistoryAsync(2).ConfigureAwait(false);
        var historyMap = allHistory
            .GroupBy(h => h.ProviderId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var usage in currentUsages)
        {
            try
            {
                if (!historyMap.TryGetValue(usage.ProviderId, out var history) || history.Count < 2)
                {
                    this.LogInsufficientHistory(usage);
                    continue;
                }

                var current = history[0];
                var previous = history[1];
                var (isReset, resetReason) = TryDetectReset(usage, previous, current);
                if (!isReset)
                {
                    continue;
                }

                await this._database.StoreResetEventAsync(
                    usage.ProviderId,
                    usage.ProviderName,
                    previous.RequestsUsed,
                    current.RequestsUsed,
                    usage.IsQuotaBased ? "quota" : "usage").ConfigureAwait(false);

                await this.SendResetNotificationAsync(usage).ConfigureAwait(false);
                this._logger.LogInformation("{ProviderId} reset: {Reason}", usage.ProviderId, resetReason);
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Reset check failed for {ProviderId}: {Message}", usage.ProviderId, ex.Message);
            }
        }
    }

    private static (bool IsReset, string Reason) TryDetectReset(ProviderUsage usage, ProviderUsage previous, ProviderUsage current)
    {
        if (current.NextResetTime.HasValue &&
            previous.NextResetTime.HasValue &&
            current.NextResetTime.Value > previous.NextResetTime.Value.AddMinutes(1))
        {
            return (true, $"Reset detected via schedule: {previous.NextResetTime:HH:mm} -> {current.NextResetTime:HH:mm}");
        }

        if (usage.IsQuotaBased)
        {
            var previousUsedPercent = UsageMath.GetEffectiveUsedPercent(previous);
            var currentUsedPercent = UsageMath.GetEffectiveUsedPercent(current);
            if (previousUsedPercent > 50 && currentUsedPercent < previousUsedPercent * 0.3)
            {
                return (true, $"Quota reset: {previousUsedPercent:F1}% -> {currentUsedPercent:F1}% used");
            }

            return (false, string.Empty);
        }

        if (previous.RequestsUsed > current.RequestsUsed)
        {
            var dropPercent = (previous.RequestsUsed - current.RequestsUsed) / previous.RequestsUsed * 100;
            if (dropPercent > 20)
            {
                return (true, $"Usage reset: ${previous.RequestsUsed:F2} -> ${current.RequestsUsed:F2} ({dropPercent:F0}% drop)");
            }
        }

        return (false, string.Empty);
    }

    private async Task SendResetNotificationAsync(ProviderUsage usage)
    {
        var prefs = await this._configService.GetPreferencesAsync().ConfigureAwait(false);
        if (!prefs.EnableNotifications || !prefs.NotifyOnQuotaExceeded || IsInQuietHours(prefs))
        {
            return;
        }

        var configs = await this._configService.GetConfigsAsync().ConfigureAwait(false);
        var config = configs.FirstOrDefault(c => c.ProviderId.Equals(usage.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (config == null || !config.EnableNotifications)
        {
            return;
        }

        var details = usage.IsQuotaBased ? "Quota reset detected." : "Usage reset detected.";
        this._notificationService.ShowQuotaExceeded(usage.ProviderName, details);
    }

    private void LogInsufficientHistory(ProviderUsage usage)
    {
        if (usage.ProviderId.Contains('.') || usage.NextResetTime != null)
        {
            this._logger.LogTrace("{ProviderId}: Initial record stored, waiting for history", usage.ProviderId);
            return;
        }

        this._logger.LogDebug("{ProviderId}: Not enough history for reset detection", usage.ProviderId);
    }

    private static bool IsInQuietHours(AppPreferences prefs)
    {
        if (!prefs.EnableQuietHours)
        {
            return false;
        }

        if (!TimeSpan.TryParse(prefs.QuietHoursStart, out var start) ||
            !TimeSpan.TryParse(prefs.QuietHoursEnd, out var end))
        {
            return false;
        }

        var now = DateTime.Now.TimeOfDay;
        if (start == end)
        {
            return true;
        }

        if (start < end)
        {
            return now >= start && now < end;
        }

        return now >= start || now < end;
    }
}
