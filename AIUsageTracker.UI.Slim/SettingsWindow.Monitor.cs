// <copyright file="SettingsWindow.Monitor.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow
{
    private async Task UpdateMonitorStatusAsync()
    {
        try
        {
            // Check if agent is running
            var isRunning = await this._monitorLifecycleService.IsAgentRunningAsync().ConfigureAwait(true);

            // Get the actual port from the agent
            int port = await this._monitorLifecycleService.GetAgentPortAsync().ConfigureAwait(true);

            if (this.MonitorStatusText != null)
            {
                this.MonitorStatusText.Text = isRunning ? "Running" : "Not Running";
            }

            // Update port display
            if (this.FindName("MonitorPortText") is TextBlock portText)
            {
                portText.Text = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to update monitor status");
            if (this.MonitorStatusText != null)
            {
                this.MonitorStatusText.Text = "Error";
            }
        }
        finally
        {
            this.RefreshDiagnosticsLog();
        }
    }

    private void RefreshDiagnosticsLog()
    {
        if (this.MonitorLogsText == null)
        {
            return;
        }

        if (this._isDeterministicScreenshotMode)
        {
            this.MonitorLogsText.Text = "Monitor health check: OK" + Environment.NewLine +
                                 "Diagnostics available in Settings > Monitor.";
            this.MonitorLogsText.ScrollToEnd();
            return;
        }

        var logs = MonitorService.DiagnosticsLog;
        var lines = new List<string>();
        if (logs.Count == 0)
        {
            lines.Add("No diagnostics captured yet.");
        }
        else
        {
            lines.AddRange(logs);
        }

        var telemetry = MonitorService.GetTelemetrySnapshot();
        lines.Add("---- Slim Telemetry ----");
        lines.Add(
            $"Usage: count={telemetry.UsageRequestCount}, avg={telemetry.UsageAverageLatencyMs:F1}ms, last={telemetry.UsageLastLatencyMs}ms, errors={telemetry.UsageErrorCount} ({telemetry.UsageErrorRatePercent:F1}%)");
        lines.Add(
            $"Refresh: count={telemetry.RefreshRequestCount}, avg={telemetry.RefreshAverageLatencyMs:F1}ms, last={telemetry.RefreshLastLatencyMs}ms, errors={telemetry.RefreshErrorCount} ({telemetry.RefreshErrorRatePercent:F1}%)");

        this.MonitorLogsText.Text = string.Join(Environment.NewLine, lines);
        this.MonitorLogsText.ScrollToEnd();
    }

    private async void RestartMonitorBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Kill any running agent process
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("AIUsageTracker.Monitor")
                .Concat(System.Diagnostics.Process.GetProcessesByName("AIUsageTracker.Monitor")))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    this._logger.LogDebug(ex, "Failed to terminate monitor process {ProcessId}", process.Id);
                }
            }

            await Task.Delay(1000);

            // Restart agent
            if (await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(true))
            {
                MessageBox.Show(
                    "Monitor restarted successfully.",
                    "Restart Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "Failed to restart Monitor.",
                    "Restart Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to restart Monitor: {ex.Message}",
                "Restart Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            this.RefreshDiagnosticsLog();
        }
    }

    private async void CheckHealthBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, port) = await this._monitorLifecycleService.IsAgentRunningWithPortAsync().ConfigureAwait(true);
            var healthSnapshot = await this._monitorService.GetHealthSnapshotAsync();
            var status = isRunning ? "Running" : "Not Running";

            MessageBox.Show(
                this.BuildHealthCheckMessage(status, port, healthSnapshot),
                "Health Check",
                MessageBoxButton.OK,
                this.GetHealthCheckIcon(isRunning, healthSnapshot));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to check health: {ex.Message}",
                "Health Check Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            this.RefreshDiagnosticsLog();
        }
    }

    private string BuildHealthCheckMessage(string processStatus, int port, MonitorHealthSnapshot? healthSnapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Monitor Status: {processStatus}"));
        builder.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Port: {port}"));

        if (healthSnapshot == null)
        {
            return builder.ToString();
        }

        builder.AppendLine($"Service Health: {healthSnapshot.ServiceHealth}");
        builder.AppendLine($"Monitor Version: {healthSnapshot.AgentVersion ?? "unknown"}");
        var contractVersion = healthSnapshot.EffectiveContractVersion ?? "unknown";
        builder.AppendLine($"API Contract: {contractVersion}");
        if (!string.IsNullOrWhiteSpace(healthSnapshot.EffectiveMinClientContractVersion))
        {
            builder.AppendLine($"Min Client Contract: {healthSnapshot.EffectiveMinClientContractVersion}");
        }

        builder.AppendLine($"Last Health Ping: {FormatHealthTimestamp(healthSnapshot.Timestamp)}");
        builder.AppendLine($"Refresh Status: {healthSnapshot.RefreshHealth.Status}");
        builder.AppendLine($"Last Refresh Attempt: {FormatHealthTimestamp(healthSnapshot.RefreshHealth.LastRefreshAttemptUtc)}");
        builder.AppendLine($"Last Successful Refresh: {FormatHealthTimestamp(healthSnapshot.RefreshHealth.LastSuccessfulRefreshUtc)}");
        builder.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Providers In Backoff: {healthSnapshot.RefreshHealth.ProvidersInBackoff}"));

        if (healthSnapshot.RefreshHealth.FailingProviders.Count > 0)
        {
            builder.AppendLine($"Failing Providers: {string.Join(", ", healthSnapshot.RefreshHealth.FailingProviders)}");
        }

        if (!string.IsNullOrWhiteSpace(healthSnapshot.RefreshHealth.LastError))
        {
            builder.AppendLine($"Last Refresh Error: {healthSnapshot.RefreshHealth.LastError}");
        }

        return builder.ToString();
    }

    private MessageBoxImage GetHealthCheckIcon(bool isRunning, MonitorHealthSnapshot? healthSnapshot)
    {
        if (!isRunning)
        {
            return MessageBoxImage.Warning;
        }

        return string.Equals(healthSnapshot?.ServiceHealth, "degraded", StringComparison.OrdinalIgnoreCase)
            ? MessageBoxImage.Warning
            : MessageBoxImage.Information;
    }

    private static string FormatHealthTimestamp(DateTime? timestampUtc)
    {
        if (!timestampUtc.HasValue)
        {
            return "Never";
        }

        return $"{timestampUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} (local)";
    }

    private async void ExportDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await this._monitorService.RefreshPortAsync();
            await this._monitorService.RefreshAgentInfoAsync();

            var (isRunning, port) = await this._monitorLifecycleService.IsAgentRunningWithPortAsync().ConfigureAwait(true);
            var healthSnapshot = await this._monitorService.GetHealthSnapshotAsync();
            var diagnosticsSnapshot = await this._monitorService.GetDiagnosticsSnapshotAsync();
            var healthDetails = this.SerializeBundlePayload(
                healthSnapshot,
                "Health payload unavailable.");
            var diagnosticsDetails = this.SerializeBundlePayload(
                diagnosticsSnapshot,
                "Diagnostics payload unavailable.");

            var saveDialog = new SaveFileDialog
            {
                FileName = $"ai-usage-tracker-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                AddExtension = true,
            };

            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            var telemetry = MonitorService.GetTelemetrySnapshot();
            var bundle = new StringBuilder();
            bundle.AppendLine("AI Usage Tracker - Diagnostics Bundle");
            bundle.AppendLine($"GeneratedAtUtc: {DateTime.UtcNow:O}");
            bundle.AppendLine($"SlimVersion: {typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "unknown"}");
            bundle.AppendLine($"AgentUrl: {this._monitorService.AgentUrl}");
            bundle.AppendLine($"AgentRunning: {isRunning}");
            bundle.AppendLine($"AgentPort: {port.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Health Summary ===");
            bundle.AppendLine(this.BuildHealthCheckMessage(isRunning ? "Running" : "Not Running", port, healthSnapshot).TrimEnd());
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Health ===");
            bundle.AppendLine(healthDetails);
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Diagnostics ===");
            this.AppendMonitorDiagnosticsSummary(bundle, diagnosticsSnapshot);
            bundle.AppendLine();
            bundle.AppendLine(diagnosticsDetails);
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Errors (monitor.json) ===");
            if (this._monitorService.LastAgentErrors.Count == 0)
            {
                bundle.AppendLine("None");
            }
            else
            {
                foreach (var error in this._monitorService.LastAgentErrors)
                {
                    bundle.AppendLine($"- {error}");
                }
            }

            bundle.AppendLine();

            bundle.AppendLine("=== Slim Telemetry ===");
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "Usage: count={0}, avg={1:F1}ms, last={2}ms, errors={3} ({4:F1}%)\r\n",
                telemetry.UsageRequestCount,
                telemetry.UsageAverageLatencyMs,
                telemetry.UsageLastLatencyMs,
                telemetry.UsageErrorCount,
                telemetry.UsageErrorRatePercent);
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "Refresh: count={0}, avg={1:F1}ms, last={2}ms, errors={3} ({4:F1}%)\r\n",
                telemetry.RefreshRequestCount,
                telemetry.RefreshAverageLatencyMs,
                telemetry.RefreshLastLatencyMs,
                telemetry.RefreshErrorCount,
                telemetry.RefreshErrorRatePercent);
            bundle.AppendLine();

            bundle.AppendLine("=== Slim Diagnostics Log ===");
            var diagnosticsLog = MonitorService.DiagnosticsLog;
            if (diagnosticsLog.Count == 0)
            {
                bundle.AppendLine("No diagnostics captured yet.");
            }
            else
            {
                foreach (var line in diagnosticsLog)
                {
                    bundle.AppendLine(line);
                }
            }

            await File.WriteAllTextAsync(saveDialog.FileName, bundle.ToString());
            MessageBox.Show(
                $"Diagnostics bundle saved to:\n{saveDialog.FileName}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to export diagnostics bundle: {ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            this.RefreshDiagnosticsLog();
        }
    }

    private string SerializeBundlePayload<T>(T? payload, string emptyFallback)
    {
        if (payload == null)
        {
            return emptyFallback;
        }

        return JsonSerializer.Serialize(payload, BundleJsonOptions);
    }

    private void AppendMonitorDiagnosticsSummary(StringBuilder bundle, AgentDiagnosticsSnapshot? diagnostics)
    {
        if (diagnostics == null)
        {
            bundle.AppendLine("Summary unavailable (typed diagnostics not available).");
            return;
        }

        bundle.AppendLine("Summary:");
        bundle.AppendFormat(
            System.Globalization.CultureInfo.InvariantCulture,
            "- Endpoint: port={0}, pid={1}, runtime={2}, args={3}\r\n",
            diagnostics.Port,
            diagnostics.ProcessId,
            diagnostics.Runtime,
            diagnostics.Args.Count);

        if (diagnostics.RefreshTelemetry != null)
        {
            var refresh = diagnostics.RefreshTelemetry;
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "- Refresh telemetry: count={0}, success={1}, failure={2}, error_rate={3:F1}%, avg={4:F1}ms, last={5}ms\r\n",
                refresh.RefreshCount,
                refresh.RefreshSuccessCount,
                refresh.RefreshFailureCount,
                refresh.ErrorRatePercent,
                refresh.AverageLatencyMs,
                refresh.LastLatencyMs);
        }

        if (diagnostics.SchedulerTelemetry != null)
        {
            var scheduler = diagnostics.SchedulerTelemetry;
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "- Scheduler telemetry: queued={0} (h={1}, n={2}, l={3}), recurring={4}, executed={5}, failed={6}, enqueued={7}, dequeued={8}, coalesced_skipped={9}, noop_signals={10}, in_flight={11}\r\n",
                scheduler.TotalQueuedJobs,
                scheduler.HighPriorityQueuedJobs,
                scheduler.NormalPriorityQueuedJobs,
                scheduler.LowPriorityQueuedJobs,
                scheduler.RecurringJobs,
                scheduler.ExecutedJobs,
                scheduler.FailedJobs,
                scheduler.EnqueuedJobs,
                scheduler.DequeuedJobs,
                scheduler.CoalescedSkippedJobs,
                scheduler.DispatchNoopSignals,
                scheduler.InFlightJobs);
        }

        if (diagnostics.PipelineTelemetry != null)
        {
            var pipeline = diagnostics.PipelineTelemetry;
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "- Pipeline telemetry: processed={0}, accepted={1}, rejected={2}, invalid_identity={3}, inactive_filtered={4}, placeholders={5}, detail_adjusted={6}, normalized={7}, privacy_redacted={8}, last_run={9}/{10}\r\n",
                pipeline.TotalProcessedEntries,
                pipeline.TotalAcceptedEntries,
                pipeline.TotalRejectedEntries,
                pipeline.InvalidIdentityCount,
                pipeline.InactiveProviderFilteredCount,
                pipeline.PlaceholderFilteredCount,
                pipeline.DetailContractAdjustedCount,
                pipeline.NormalizedCount,
                pipeline.PrivacyRedactedCount,
                pipeline.LastRunAcceptedEntries,
                pipeline.LastRunTotalEntries);
        }

        if (diagnostics.Observability?.ActivitySourceNames.Count > 0)
        {
            bundle.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "- Observability: activity_sources={0}\r\n",
                string.Join(", ", diagnostics.Observability.ActivitySourceNames));
        }
    }
}
