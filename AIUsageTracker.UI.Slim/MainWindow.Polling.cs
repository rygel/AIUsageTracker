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
    private async Task RapidPollUntilDataAvailableAsync()
    {
        const int maxAttempts = 15;
        const int pollIntervalMs = 2000; // 2 seconds between attempts

        this.LogDiagnostic("[DIAGNOSTIC] RapidPollUntilDataAvailableAsync starting...");
        this.ShowStatus("Loading data...", StatusType.Info);

        // First, check if Monitor is reachable
        this.LogDiagnostic("[DIAGNOSTIC] Checking Monitor health...");
        var isHealthy = await this._monitorService.CheckHealthAsync();
        this.LogDiagnostic($"[DIAGNOSTIC] Monitor health: {isHealthy}");

        if (!isHealthy)
        {
            await this._monitorService.RefreshAgentInfoAsync();
            this.ShowStatus("Monitor not reachable", StatusType.Error);
            this.ShowErrorState(
                BuildMonitorErrorMessage(
                    "Cannot connect to Monitor.",
                    "Please ensure:\n1. Monitor is running\n2. Port is correct (check monitor.json)\n3. Firewall is not blocking\n\nTry restarting the Monitor.",
                    this._monitorService.LastAgentErrors));

            return;
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            this.LogDiagnostic($"[DIAGNOSTIC] Poll attempt {attempt + 1}/{maxAttempts}");

            try
            {
                // Try to get cached data from monitor
                this.LogDiagnostic("[DIAGNOSTIC] Calling GetUsageAsync...");
                var usages = await this.GetUsageForDisplayAsync();
                this.LogDiagnostic($"[DIAGNOSTIC] GetUsageAsync returned {usages.Count} providers");

                // Show all providers from monitor (filtering already done in database)
                if (usages.Any())
                {
                    this.LogDiagnostic("[DIAGNOSTIC] Data available, rendering...");

                    // Data is available - render and stop rapid polling
                    lock (this._dataLock)
                    {
                        this._usages = usages.ToList();
                    }

                    this.RenderProviders();
                    this._lastMonitorUpdate = DateTime.Now;
                    this.ShowStatus($"{DateTime.Now:HH:mm:ss}", StatusType.Success);
                    _ = this.UpdateTrayIconsAsync();
                    this.LogDiagnostic("[DIAGNOSTIC] Data rendered successfully");
                    return;
                }

                this.LogDiagnostic("[DIAGNOSTIC] No data available");

                // No data yet - on first attempt, trigger a background refresh
                // and keep polling so data appears as soon as refresh completes.
                if (attempt == 0)
                {
                    this.LogDiagnostic("[DIAGNOSTIC] First attempt, no data - triggering background refresh...");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await this._monitorService.TriggerRefreshAsync();
                            this.LogDiagnostic("[DIAGNOSTIC] Background refresh triggered");
                        }
                        catch (Exception ex)
                        {
                            this.LogDiagnostic($"[DIAGNOSTIC] Background refresh failed: {ex.Message}");
                        }
                    });

                    // Show UI immediately with empty state
                    this.LogDiagnostic("[DIAGNOSTIC] Showing empty state...");
                    this.ShowStatus("Scanning for providers...", StatusType.Info);
                    this.LogDiagnostic("[DIAGNOSTIC] About to call RenderProviders...");
                    this.RenderProviders(); // Will show empty or loading state
                    this.LogDiagnostic("[DIAGNOSTIC] RenderProviders completed");
                }

                // No data yet - wait and try again
                if (attempt < maxAttempts - 1)
                {
                    this.ShowStatus($"Waiting for data... ({attempt + 1}/{maxAttempts})", StatusType.Warning);
                    await Task.Delay(pollIntervalMs);
                }
            }
            catch (HttpRequestException ex)
            {
                this.LogDiagnostic($"[DIAGNOSTIC] Connection error: {ex.Message}");
                this.ShowStatus("Connection lost", StatusType.Error);
                this.ShowErrorState($"Lost connection to Monitor:\n{ex.Message}\n\nTry refreshing or restarting the Monitor.");

                return;
            }
            catch (Exception ex)
            {
                this.LogDiagnostic($"[DIAGNOSTIC] Error: {ex.Message}");
                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(pollIntervalMs);
                }
            }
        }

        this.LogDiagnostic("[DIAGNOSTIC] Max attempts reached, no data available");
        this.ShowStatus("No data available", StatusType.Error);
        this.ShowErrorState("No provider data available.\n\nThe Monitor may still be initializing.\nTry refreshing manually or check Settings > Monitor.");
    }

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
            await this._monitorService.TriggerRefreshAsync();

            // Get updated usage data
            var latestUsages = await this.GetUsageForDisplayAsync();
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
                lock (this._dataLock)
                {
                    this._usages = latestUsages.ToList();
                }

                this.RenderProviders();
                this._lastMonitorUpdate = now;
                this.ShowStatus($"{now:HH:mm:ss}", StatusType.Success);
                _ = this.UpdateTrayIconsAsync();
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
        catch (Exception ex)
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
        var groupedSnapshot = await this._monitorService.GetGroupedUsageAsync();
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

            // Poll monitor for fresh data
            try
            {
                var usages = await this.GetUsageForDisplayAsync();

                // Show all providers from monitor (filtering already done in database)
                if (usages.Any())
                {
                    // Fresh data received - update UI
                    await this.Dispatcher.InvokeAsync(async () =>
                    {
                        await this.FetchDataAsync();
                    });
                }
                else
                {
                    // Empty data - try to trigger a refresh if cooldown has passed
                    // This handles cases where Monitor restarted or hasn't completed its background refresh
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
                            await this._monitorService.TriggerRefreshAsync();
                        }
                        catch (Exception ex)
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

                    // Wait a moment and retry getting data
                    await Task.Delay(1000);
                    await this.Dispatcher.InvokeAsync(async () =>
                    {
                        await this.FetchDataAsync(" (refreshed)");
                    });

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
            catch (Exception ex)
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
        catch (Exception ex)
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
            var usages = await this.GetUsageForDisplayAsync();
            if (usages.Any())
            {
                var now = DateTime.Now;
                var successStatusMessage = $"{now:HH:mm:ss}{statusSuffix}";
                var switchToNormalInterval = this._pollingTimer != null
                    && this._pollingTimer.Interval != NormalPollingInterval;

                lock (this._dataLock)
                {
                    this._usages = usages.ToList();
                }

                this.RenderProviders();
                this._lastMonitorUpdate = now;
                this.ShowStatus(successStatusMessage, StatusType.Success);
                _ = this.UpdateTrayIconsAsync();

                if (switchToNormalInterval && this._pollingTimer != null)
                {
                    this._pollingTimer.Interval = NormalPollingInterval;
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "FetchDataAsync failed");
        }
        finally
        {
            this._isPollingInProgress = false;
        }
    }

    private string FormatMonitorOfflineStatus()
    {
        return MainWindowRuntimeLogic.FormatMonitorOfflineStatus(this._lastMonitorUpdate, DateTime.Now);
    }
}
