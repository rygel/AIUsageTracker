// <copyright file="MainWindow.Polling.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class MainWindow : Window
{
    private async Task RefreshDataAsync()
    {
        if (this._isLoading)
        {
            return;
        }

        try
        {
            this._isLoading = true;
            this.ShowStatus("Refreshing...", StatusType.Info);

            // Trigger refresh on monitor
            await this._monitorService.TriggerRefreshAsync().ConfigureAwait(true);

            // Get updated usage data
            var latestUsages = await this.GetUsageForDisplayAsync().ConfigureAwait(true);
            var now = DateTime.Now;
            var hasLatestUsages = latestUsages.Any();
            bool hasCurrentUsages = false;
            if (!hasLatestUsages)
            {
                lock (this._dataLock)
                {
                    hasCurrentUsages = this._usages.Any();
                }
            }

            if (hasLatestUsages)
            {
                this.ApplyFetchedUsages(latestUsages, now);
            }
            else if (hasCurrentUsages)
            {
                this.ShowStatus("Refresh returned no data, keeping last snapshot", StatusType.Warning);
            }
            else
            {
                this.ShowErrorState("No provider data available.\n\nMonitor may still be initializing.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.ShowErrorState($"Refresh failed: {ex.Message}");
        }
        finally
        {
            this._isLoading = false;
        }
    }

    private async Task<IReadOnlyList<ProviderUsage>> GetUsageForDisplayAsync()
    {
        var groupedSnapshot = await this._monitorService.GetGroupedUsageAsync().ConfigureAwait(true);
        if (groupedSnapshot == null)
        {
            this._logger.LogWarning("Grouped usage snapshot is unavailable.");
            return Array.Empty<ProviderUsage>();
        }

        return GroupedUsageDisplayAdapter.Expand(groupedSnapshot);
    }

    private void StartPollingTimer()
    {
        this._pollingTimer?.Stop();

        bool hasUsages;
        lock (this._dataLock)
        {
            hasUsages = this._usages.Any();
        }

        this._pollingTimer = new DispatcherTimer
        {
            Interval = hasUsages ? NormalPollingInterval : StartupPollingInterval,
        };

        this._pollingTimer.Tick += async (s, e) =>
        {
            if (this._isPollingInProgress)
            {
                return;
            }

            this._isPollingInProgress = true;
            try
            {
                var usages = await this.GetUsageForDisplayAsync().ConfigureAwait(true);

                if (usages.Any())
                {
                    this.ApplyFetchedUsages(usages, DateTime.Now);
                }
                else
                {
                    var refreshDecision = MainWindowRuntimeLogic.CreatePollingRefreshDecision(
                        this._lastRefreshTrigger,
                        DateTime.Now,
                        RefreshCooldownSeconds);
                    if (refreshDecision.ShouldTriggerRefresh)
                    {
                        this._logger.LogDebug("Polling returned empty, triggering refresh");
                        this._lastRefreshTrigger = DateTime.Now;
                        try
                        {
                            await this._monitorService.TriggerRefreshAsync().ConfigureAwait(true);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            this._logger.LogWarning(ex, "TriggerRefreshAsync failed during polling retry");
                        }
                    }
                    else
                    {
                        this._logger.LogDebug(
                            "Polling returned empty, refresh cooldown active ({SecondsSinceLastRefresh:F0}s ago)",
                            refreshDecision.SecondsSinceLastRefresh);
                    }

                    bool hasCurrentUsages;
                    lock (this._dataLock)
                    {
                        hasCurrentUsages = this._usages.Any();
                    }

                    var now = DateTime.Now;
                    string? noDataMessage = null;
                    StatusType? noDataStatusType = null;
                    var switchToStartupInterval = false;
                    if (!hasCurrentUsages)
                    {
                        noDataMessage = "No data - waiting for Monitor";
                        noDataStatusType = StatusType.Warning;
                        switchToStartupInterval = true;
                    }
                    else if ((now - this._lastMonitorUpdate).TotalMinutes > 5)
                    {
                        noDataMessage = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(this._lastMonitorUpdate, now);
                        noDataStatusType = StatusType.Warning;
                    }

                    if (noDataMessage != null && noDataStatusType.HasValue)
                    {
                        this.ShowStatus(noDataMessage, noDataStatusType.Value);
                    }

                    if (switchToStartupInterval &&
                        this._pollingTimer != null &&
                        this._pollingTimer.Interval != StartupPollingInterval)
                    {
                        this._pollingTimer.Interval = StartupPollingInterval;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this._logger.LogWarning(ex, "Polling loop error");
                bool hasOldData;
                lock (this._dataLock)
                {
                    hasOldData = this._usages.Any();
                }

                var now = DateTime.Now;
                string? exceptionMessage;
                StatusType exceptionStatusType;
                var switchToStartupInterval = false;
                if (hasOldData)
                {
                    exceptionMessage = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(this._lastMonitorUpdate, now);
                    exceptionStatusType = StatusType.Warning;
                }
                else
                {
                    exceptionMessage = "Connection error";
                    exceptionStatusType = StatusType.Error;
                    switchToStartupInterval = true;
                }

                this.ShowStatus(exceptionMessage, exceptionStatusType);

                if (switchToStartupInterval &&
                    this._pollingTimer != null &&
                    this._pollingTimer.Interval != StartupPollingInterval)
                {
                    this._pollingTimer.Interval = StartupPollingInterval;
                }
            }
            finally
            {
                this._isPollingInProgress = false;
            }
        };

        this._pollingTimer.Start();
    }

    private async Task UpdateTrayIconsAsync()
    {
        if (Application.Current is not App app)
        {
            return;
        }

        if (this._isTrayIconUpdateInProgress)
        {
            return;
        }

        this._isTrayIconUpdateInProgress = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            bool hasCachedConfigs;
            lock (this._dataLock)
            {
                hasCachedConfigs = this._configs.Any();
            }

            var shouldRefreshConfigs = MainWindowRuntimeLogic.ShouldRefreshTrayConfigs(
                hasCachedConfigs: hasCachedConfigs,
                lastRefreshUtc: this._lastTrayConfigRefresh,
                nowUtc: DateTime.UtcNow,
                refreshInterval: TrayConfigRefreshInterval);

            if (shouldRefreshConfigs)
            {
                var configs = (await this._monitorService.GetConfigsAsync().ConfigureAwait(true)).ToList();
                lock (this._dataLock)
                {
                    this._configs = configs;
                }

                this._lastTrayConfigRefresh = DateTime.UtcNow;
            }

            List<ProviderUsage> usagesCopy;
            List<ProviderConfig> configsCopy;
            lock (this._dataLock)
            {
                usagesCopy = this._usages.ToList();
                configsCopy = this._configs.ToList();
            }

            app.UpdateProviderTrayIcons(usagesCopy, configsCopy, this._preferences);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.LogDiagnostic($"[DIAGNOSTIC] UpdateTrayIconsAsync failed: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            this.LogDiagnostic($"[DIAGNOSTIC] UpdateTrayIconsAsync completed in {stopwatch.ElapsedMilliseconds}ms");
            this._isTrayIconUpdateInProgress = false;
        }
    }

    private async Task FetchDataAsync(string statusSuffix = "")
    {
        if (this._isPollingInProgress)
        {
            return;
        }

        this._isPollingInProgress = true;
        try
        {
            var usages = await this.GetUsageForDisplayAsync().ConfigureAwait(true);
            if (usages.Any())
            {
                this.ApplyFetchedUsages(usages, DateTime.Now, statusSuffix);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogWarning(ex, "FetchDataAsync failed");
        }
        finally
        {
            this._isPollingInProgress = false;
        }
    }

    private void ApplyFetchedUsages(IReadOnlyList<ProviderUsage> usages, DateTime now, string statusSuffix = "")
    {
        var switchToNormalInterval = this._pollingTimer != null
            && this._pollingTimer.Interval != NormalPollingInterval;

        lock (this._dataLock)
        {
            this._usages = usages.ToList();
        }

        this.RenderProviders();
        this._lastMonitorUpdate = now;
        this.ShowStatus($"{now:HH:mm:ss}{statusSuffix}", StatusType.Success);
        _ = this.UpdateTrayIconsAsync();

        if (switchToNormalInterval && this._pollingTimer != null)
        {
            this._pollingTimer.Interval = NormalPollingInterval;
        }
    }
}
