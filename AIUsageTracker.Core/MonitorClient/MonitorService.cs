// <copyright file="MonitorService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Core.MonitorClient;

public class MonitorService : IMonitorService
{
    public const string ExpectedApiContractVersion = MonitorApiContract.CurrentVersion;

    private const int UsageRequestTimeoutSeconds = 8;
    private const int ConfigRequestTimeoutSeconds = 3;

    private static readonly List<string> _diagnosticsLog = new();
    private static readonly ActivitySource ActivitySource = new("AIUsageTracker.Core.MonitorService");

    private long _usageRequestCount;
    private long _usageErrorCount;
    private long _usageTotalLatencyMs;
    private long _usageLastLatencyMs;
    private long _refreshRequestCount;
    private long _refreshErrorCount;
    private long _refreshTotalLatencyMs;
    private long _refreshLastLatencyMs;
    private readonly object _groupedUsageCacheLock = new();
    private AgentGroupedUsageSnapshot? _cachedGroupedUsageSnapshot;
    private string? _cachedGroupedUsageETag;

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<MonitorService>? _logger;
    private readonly IMonitorLauncher _monitorLauncher;

    public MonitorService()
        : this(CreateDefaultHttpClient(), null)
    {
    }

    public MonitorService(HttpClient httpClient, ILogger<MonitorService>? logger, IMonitorLauncher? monitorLauncher = null)
    {
        this._httpClient = httpClient;
        this._logger = logger;
        this._monitorLauncher = monitorLauncher ?? new MonitorLauncher(logger: null);
        this._jsonOptions = MonitorJsonSerializer.DefaultOptions;

        // Note: Port discovery is now done explicitly via RefreshPortAsync()
        // to avoid race conditions where the Monitor port changes
    }

    public static IReadOnlyList<string> DiagnosticsLog => _diagnosticsLog;

    /// <inheritdoc/>
    public string AgentUrl { get; set; } = "http://localhost:5000";

    /// <inheritdoc/>
    public IReadOnlyList<string> LastAgentErrors { get; private set; } = new List<string>();

    public static void LogDiagnostic(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        lock (_diagnosticsLog)
        {
            _diagnosticsLog.Add($"[{timestamp}] {message}");
            if (_diagnosticsLog.Count > 100)
            {
                _diagnosticsLog.RemoveAt(0);
            }
        }

        System.Diagnostics.Debug.WriteLine($"[{timestamp}] [DIAG] {message}");
    }

    public AgentTelemetrySnapshot GetTelemetrySnapshot()
    {
        var usageRequestCount = Interlocked.Read(ref this._usageRequestCount);
        var usageErrorCount = Interlocked.Read(ref this._usageErrorCount);
        var usageTotalLatencyMs = Interlocked.Read(ref this._usageTotalLatencyMs);
        var usageLastLatencyMs = Interlocked.Read(ref this._usageLastLatencyMs);
        var refreshRequestCount = Interlocked.Read(ref this._refreshRequestCount);
        var refreshErrorCount = Interlocked.Read(ref this._refreshErrorCount);
        var refreshTotalLatencyMs = Interlocked.Read(ref this._refreshTotalLatencyMs);
        var refreshLastLatencyMs = Interlocked.Read(ref this._refreshLastLatencyMs);

        return new AgentTelemetrySnapshot
        {
            UsageRequestCount = usageRequestCount,
            UsageErrorCount = usageErrorCount,
            UsageAverageLatencyMs = usageRequestCount == 0 ? 0 : usageTotalLatencyMs / (double)usageRequestCount,
            UsageLastLatencyMs = usageLastLatencyMs,
            UsageErrorRatePercent = usageRequestCount == 0 ? 0 : (usageErrorCount / (double)usageRequestCount) * 100.0,
            RefreshRequestCount = refreshRequestCount,
            RefreshErrorCount = refreshErrorCount,
            RefreshAverageLatencyMs = refreshRequestCount == 0 ? 0 : refreshTotalLatencyMs / (double)refreshRequestCount,
            RefreshLastLatencyMs = refreshLastLatencyMs,
            RefreshErrorRatePercent = refreshRequestCount == 0 ? 0 : (refreshErrorCount / (double)refreshRequestCount) * 100.0,
        };
    }

    public static AgentContractHandshakeResult EvaluateApiContractCompatibility(
        string? contractVersion,
        string? minClientContractVersion,
        string? reportedAgentVersion)
    {
        return MonitorApiContractEvaluator.Evaluate(
            contractVersion,
            minClientContractVersion,
            reportedAgentVersion,
            ExpectedApiContractVersion);
    }

    /// <inheritdoc/>
    public async Task RefreshAgentInfoAsync()
    {
        LogDiagnostic("Refreshing Monitor Info from file...");
        try
        {
            var metadata = await this._monitorLauncher.GetMonitorMetadataSnapshotAsync().ConfigureAwait(false);
            if (metadata.IsUsable && metadata.Info != null)
            {
                var info = metadata.Info;
                if (info.Port > 0)
                {
                    this.AgentUrl = $"http://localhost:{info.Port}";
                    LogDiagnostic($"Found Monitor running on port {info.Port} from monitor.json");
                }

                this.LastAgentErrors = info.Errors ?? new List<string>();
                return;
            }

            var preservedErrors = GetActionableMetadataErrors(metadata.Info?.Errors);
            LogDiagnostic("monitor.json missing, stale, or invalid; using default port 5000");
            this.AgentUrl = "http://localhost:5000";
            this.LastAgentErrors = preservedErrors;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            this._logger?.LogWarning(ex, "Error refreshing monitor info");
            this.AgentUrl = "http://localhost:5000";
            this.LastAgentErrors = new List<string>();
        }
    }

    /// <inheritdoc/>
    public async Task RefreshPortAsync()
    {
        using var activity = ActivitySource.StartActivity("monitor.refresh_port", ActivityKind.Internal);
        activity?.SetTag("monitor.agent_url.before", this.AgentUrl);
        var status = await this._monitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        activity?.SetTag("monitor.is_running", status.IsRunning);
        activity?.SetTag("monitor.port", status.Port);
        if (!status.IsRunning)
        {
            MonitorService.LogDiagnostic(
                $"{status.Message} Keeping existing Monitor endpoint {this.AgentUrl}.");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return;
        }

        this.AgentUrl = $"http://localhost:{status.Port}";
        MonitorService.LogDiagnostic($"Using Monitor endpoint {this.AgentUrl}.");
        activity?.SetTag("monitor.agent_url.after", this.AgentUrl);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    // Provider usage endpoints

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderUsage>> GetUsageAsync()
    {
        using var activity = ActivitySource.StartActivity("monitor.get_usage", ActivityKind.Client);
        activity?.SetTag("monitor.agent_url", this.AgentUrl);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await this.RefreshPortAsync().ConfigureAwait(false);
            var usage = await this.GetUsageOnceAsync().ConfigureAwait(false);
            LogDiagnostic($"Successfully fetched usage from {this.AgentUrl}");
            stopwatch.Stop();
            this.RecordUsageTelemetry(stopwatch.Elapsed, true);
            activity?.SetTag("monitor.usage_count", usage?.Count ?? 0);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return usage ?? new List<ProviderUsage>();
        }
        catch (Exception ex) when (IsRecoverableUsageFailure(ex))
        {
            LogDiagnostic($"{DescribeUsageFailure(ex)} to {this.AgentUrl}, refreshing port and retrying...");
            await this.RefreshPortAsync().ConfigureAwait(false);

            try
            {
                var usage = await this.GetUsageOnceAsync().ConfigureAwait(false);
                LogDiagnostic($"Successfully fetched usage from {this.AgentUrl} after port refresh");
                stopwatch.Stop();
                this.RecordUsageTelemetry(stopwatch.Elapsed, true);
                activity?.SetTag("monitor.usage_count", usage?.Count ?? 0);
                activity?.SetTag("monitor.retry", true);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return usage ?? new List<ProviderUsage>();
            }
            catch (Exception retryEx) when (IsRecoverableUsageFailure(retryEx))
            {
                stopwatch.Stop();
                this.RecordUsageTelemetry(stopwatch.Elapsed, false);
                LogDiagnostic($"Failed to fetch usage from {this.AgentUrl} after port refresh: {DescribeUsageFailure(retryEx)}");
                activity?.SetTag("error.type", retryEx.GetType().Name);
                activity?.SetStatus(ActivityStatusCode.Error, retryEx.Message);
                return new List<ProviderUsage>();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            stopwatch.Stop();
            this.RecordUsageTelemetry(stopwatch.Elapsed, false);
            LogDiagnostic($"Failed to fetch usage from {this.AgentUrl}: {ex.Message}");
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new List<ProviderUsage>();
        }
    }

    /// <inheritdoc/>
    public async Task<AgentGroupedUsageSnapshot?> GetGroupedUsageAsync()
    {
        await this.RefreshPortAsync().ConfigureAwait(false);

        try
        {
            using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(UsageRequestTimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Get, this.BuildMonitorUrl(MonitorApiRoutes.UsageGrouped));

            var cachedETag = this.GetCachedGroupedUsageETag();
            if (!string.IsNullOrWhiteSpace(cachedETag) &&
                EntityTagHeaderValue.TryParse(cachedETag, out var entityTag))
            {
                request.Headers.IfNoneMatch.Add(entityTag);
            }

            using var response = await this._httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                var cachedSnapshot = this.GetCachedGroupedUsageSnapshot();
                if (cachedSnapshot != null)
                {
                    return cachedSnapshot;
                }

                this._logger?.LogWarning(
                    "GetGroupedUsageAsync received 304 without a cached snapshot from {AgentUrl}",
                    this.AgentUrl);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                this._logger?.LogWarning(
                    "GetGroupedUsageAsync returned status {StatusCode} from {AgentUrl}",
                    (int)response.StatusCode,
                    this.AgentUrl);
                return null;
            }

            var snapshot = await this.ReadMonitorResponseJsonAsync<AgentGroupedUsageSnapshot>(
                response,
                nameof(this.GetGroupedUsageAsync)).ConfigureAwait(false);

            if (snapshot != null)
            {
                this.CacheGroupedUsageSnapshot(snapshot, response.Headers.ETag?.ToString());
            }

            return snapshot;
        }
        catch (TaskCanceledException ex)
        {
            this._logger?.LogWarning(
                ex,
                "{Operation} timeout after {TimeoutSeconds}s at {Url}",
                nameof(this.GetGroupedUsageAsync),
                UsageRequestTimeoutSeconds,
                this.BuildMonitorUrl(MonitorApiRoutes.UsageGrouped));
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            this._logger?.LogWarning(
                ex,
                "{Operation} failed at {Url}",
                nameof(this.GetGroupedUsageAsync),
                this.BuildMonitorUrl(MonitorApiRoutes.UsageGrouped));
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<ProviderUsage?> GetUsageByProviderAsync(string providerId)
    {
        return await this.GetFromMonitorJsonAsync<ProviderUsage>(
            MonitorApiRoutes.UsageByProvider(providerId),
            nameof(this.GetUsageByProviderAsync)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        var history = await this.GetFromMonitorJsonAsync<List<ProviderUsage>>(
            MonitorApiRoutes.HistoryWithLimit(limit),
            nameof(this.GetHistoryAsync)).ConfigureAwait(false);
        return history ?? new List<ProviderUsage>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100)
    {
        var history = await this.GetFromMonitorJsonAsync<List<ProviderUsage>>(
            MonitorApiRoutes.HistoryByProviderWithLimit(providerId, limit),
            nameof(this.GetHistoryByProviderAsync)).ConfigureAwait(false);
        return history ?? new List<ProviderUsage>();
    }

    /// <inheritdoc/>
    public async Task<bool> TriggerRefreshAsync()
    {
        using var activity = ActivitySource.StartActivity("monitor.trigger_refresh", ActivityKind.Client);
        activity?.SetTag("monitor.agent_url", this.AgentUrl);
        var stopwatch = Stopwatch.StartNew();
        await this.RefreshPortAsync().ConfigureAwait(false);
        var response = await this.SendMonitorRequestAsync(
            httpClient => httpClient.PostAsync(this.BuildMonitorUrl(MonitorApiRoutes.Refresh), null),
            nameof(this.TriggerRefreshAsync)).ConfigureAwait(false);

        stopwatch.Stop();
        if (response != null)
        {
            this.RecordRefreshTelemetry(stopwatch.Elapsed, response.IsSuccessStatusCode);
            activity?.SetTag("http.status_code", (int)response.StatusCode);
            activity?.SetStatus(response.IsSuccessStatusCode ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            return response.IsSuccessStatusCode;
        }

        this.RecordRefreshTelemetry(stopwatch.Elapsed, false);
        activity?.SetStatus(ActivityStatusCode.Error, "No response");
        return false;
    }

    // Config endpoints

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderConfig>> GetConfigsAsync()
    {
        var configs = await this.GetFromMonitorJsonAsync<List<ProviderConfig>>(
            MonitorApiRoutes.Config,
            nameof(this.GetConfigsAsync),
            ConfigRequestTimeoutSeconds).ConfigureAwait(false);
        return configs ?? new List<ProviderConfig>();
    }

    /// <inheritdoc/>
    public async Task<bool> SaveConfigAsync(ProviderConfig config)
    {
        return await this.SendMonitorStatusRequestAsync(
            httpClient => httpClient.PostAsJsonAsync(
                this.BuildMonitorUrl(MonitorApiRoutes.Config),
                config,
                this._jsonOptions),
            nameof(this.SaveConfigAsync)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveConfigAsync(string providerId)
    {
        return await this.SendMonitorStatusRequestAsync(
            httpClient => httpClient.DeleteAsync(this.BuildMonitorUrl(MonitorApiRoutes.ConfigByProvider(providerId))),
            nameof(this.RemoveConfigAsync)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SendTestNotificationAsync()
    {
        var result = await this.SendTestNotificationDetailedAsync().ConfigureAwait(false);
        return result.Success;
    }

    /// <inheritdoc/>
    public async Task<MonitorActionResult> SendTestNotificationDetailedAsync()
    {
        try
        {
            await this.RefreshPortAsync().ConfigureAwait(false);
            using var response = await this._httpClient.PostAsync(this.BuildMonitorUrl(MonitorApiRoutes.NotificationTest), null).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var result = await this.ReadMonitorResponseJsonAsync<MonitorActionResult>(
                    response,
                    nameof(this.SendTestNotificationDetailedAsync)).ConfigureAwait(false);
                return result ?? new MonitorActionResult
                {
                    Success = true,
                    Message = "Test sent. Check system notifications.",
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new MonitorActionResult
                {
                    Success = false,
                    Message = "Monitor endpoint not available. Restart Monitor and try again.",
                };
            }

            return new MonitorActionResult
            {
                Success = false,
                Message = $"Monitor returned {(int)response.StatusCode} ({response.ReasonPhrase}).",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            this._logger?.LogWarning(ex, "SendTestNotificationAsync error");
            return new MonitorActionResult
            {
                Success = false,
                Message = "Could not reach Monitor. Ensure it is running and try again.",
            };
        }
    }

    // Scan for keys endpoint

    /// <inheritdoc/>
    public async Task<AgentScanKeysResult> ScanForKeysAsync()
    {
        using var response = await this.SendMonitorRequestAsync(
            httpClient => httpClient.PostAsync(this.BuildMonitorUrl(MonitorApiRoutes.ScanKeys), null),
            nameof(this.ScanForKeysAsync)).ConfigureAwait(false);
        if (response?.IsSuccessStatusCode == true)
        {
            var result = await this.ReadMonitorResponseJsonAsync<AgentScanKeysResponse>(
                response,
                nameof(this.ScanForKeysAsync)).ConfigureAwait(false);
            if (result != null)
            {
                return new AgentScanKeysResult
                {
                    Count = result.Discovered,
                    Configs = result.Configs ?? [],
                };
            }
        }

        return new AgentScanKeysResult();
    }

    // Health check

    /// <inheritdoc/>
    public async Task<bool> CheckHealthAsync()
    {
        return await this.CheckHealthCoreAsync(cancellationToken: default).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> CheckHealthAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await this.CheckHealthCoreAsync(cts.Token).ConfigureAwait(false);
    }

    private async Task<bool> CheckHealthCoreAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("monitor.check_health", ActivityKind.Client);
        activity?.SetTag("monitor.agent_url", this.AgentUrl);
        await this.RefreshPortAsync().ConfigureAwait(false);
        var response = await this.SendMonitorRequestAsync(
            httpClient => httpClient.GetAsync(this.BuildMonitorUrl(MonitorApiRoutes.Health), cancellationToken),
            nameof(this.CheckHealthAsync)).ConfigureAwait(false);
        var success = response?.IsSuccessStatusCode == true;
        if (response != null)
        {
            activity?.SetTag("http.status_code", (int)response.StatusCode);
        }

        activity?.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        return success;
    }

    /// <inheritdoc/>
    public async Task<MonitorHealthSnapshot?> GetHealthSnapshotAsync()
    {
        using var activity = ActivitySource.StartActivity("monitor.get_health_snapshot", ActivityKind.Client);
        activity?.SetTag("monitor.agent_url", this.AgentUrl);
        await this.RefreshPortAsync().ConfigureAwait(false);
        using var response = await this.SendMonitorRequestAsync(
            httpClient => httpClient.GetAsync(this.BuildMonitorUrl(MonitorApiRoutes.Health)),
            nameof(this.GetHealthSnapshotAsync)).ConfigureAwait(false);
        if (response?.IsSuccessStatusCode != true)
        {
            if (response != null)
            {
                activity?.SetTag("http.status_code", (int)response.StatusCode);
            }

            activity?.SetStatus(ActivityStatusCode.Error, "Health endpoint unavailable");
            return null;
        }

        var snapshot = await this.ReadMonitorResponseJsonAsync<MonitorHealthSnapshot>(
            response,
            nameof(this.GetHealthSnapshotAsync)).ConfigureAwait(false);
        activity?.SetTag("monitor.health_status", snapshot?.Status);
        activity?.SetTag("monitor.service_health", snapshot?.ServiceHealth);
        activity?.SetStatus(snapshot == null ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        return snapshot;
    }

    /// <inheritdoc/>
    public async Task<AgentDiagnosticsSnapshot?> GetDiagnosticsSnapshotAsync()
    {
        return await this.GetFromMonitorJsonAsync<AgentDiagnosticsSnapshot>(
            MonitorApiRoutes.Diagnostics,
            nameof(this.GetDiagnosticsSnapshotAsync)).ConfigureAwait(false);
    }

    public Task<string> GetHealthDetailsAsync()
    {
        return this.GetEndpointDetailsAsync(MonitorApiRoutes.Health);
    }

    public Task<string> GetDiagnosticsDetailsAsync()
    {
        return this.GetEndpointDetailsAsync(MonitorApiRoutes.Diagnostics);
    }

    /// <inheritdoc/>
    public async Task<AgentContractHandshakeResult> CheckApiContractAsync()
    {
        using var activity = ActivitySource.StartActivity("monitor.check_api_contract", ActivityKind.Client);
        activity?.SetTag("monitor.agent_url", this.AgentUrl);
        try
        {
            using var response = await this._httpClient.GetAsync(this.BuildMonitorUrl(MonitorApiRoutes.Health)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                activity?.SetTag("http.status_code", (int)response.StatusCode);
                activity?.SetStatus(ActivityStatusCode.Error, "Health endpoint returned non-success");
                return new AgentContractHandshakeResult
                {
                    IsReachable = false,
                    IsCompatible = false,
                    Message = $"Agent health check failed ({(int)response.StatusCode}).",
                };
            }

            return await ParseContractResponseAsync(response.Content, activity).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger?.LogWarning(ex, "CheckApiContractAsync failed against {AgentUrl}", this.AgentUrl);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new AgentContractHandshakeResult
            {
                IsReachable = false,
                IsCompatible = false,
                Message = $"Agent API handshake failed: {ex.Message}",
            };
        }
    }

    // Diagnostics & Export
    public async Task<MonitorActionResult> CheckProviderAsync(string providerId)
    {
        try
        {
            using var response = await this._httpClient.GetAsync(this.BuildMonitorUrl(MonitorApiRoutes.ProviderCheck(providerId))).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var result = await this.ReadMonitorResponseJsonAsync<AgentProviderCheckResponse>(
                    response,
                    nameof(this.CheckProviderAsync)).ConfigureAwait(false);
                return new MonitorActionResult
                {
                    Success = result?.Success ?? false,
                    Message = result?.Message ?? "Unknown status",
                };
            }

            // Try to read error message if available
            var error = await this.ReadMonitorResponseJsonAsync<AgentProviderCheckResponse>(
                response,
                nameof(this.CheckProviderAsync)).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(error?.Message))
            {
                return new MonitorActionResult
                {
                    Success = false,
                    Message = error.Message,
                };
            }

            return new MonitorActionResult
            {
                Success = false,
                Message = $"HTTP {response.StatusCode}",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger?.LogWarning(ex, "CheckProviderAsync failed for {ProviderId}", providerId);
            return new MonitorActionResult
            {
                Success = false,
                Message = $"Connection error: {ex.Message}",
            };
        }
    }

    /// <inheritdoc/>
    public async Task<string> ExportDataAsync(string format)
    {
        using var response = await this.SendMonitorRequestAsync(
            httpClient => httpClient.GetAsync(this.BuildMonitorUrl(MonitorApiRoutes.ExportByFormat(format))),
            nameof(this.ExportDataAsync)).ConfigureAwait(false);
        if (response?.IsSuccessStatusCode == true)
        {
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        return string.Empty;
    }

    public async Task<Stream?> ExportDataAsync(string format, int days)
    {
        var response = await this.SendMonitorRequestAsync(
            httpClient => httpClient.GetAsync(this.BuildMonitorUrl(MonitorApiRoutes.ExportWithWindow(format, days))),
            nameof(this.ExportDataAsync)).ConfigureAwait(false);
        if (response?.IsSuccessStatusCode == true)
        {
            return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<AgentContractHandshakeResult> ParseContractResponseAsync(HttpContent content, Activity? activity)
    {
        var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var root = document.RootElement;

            var contractVersion = TryGetJsonString(root, MonitorApiContract.ContractVersionJsonKeys);
            var minClientContractVersion = TryGetJsonString(root, MonitorApiContract.MinClientContractVersionJsonKeys);
            var reportedAgentVersion = TryGetJsonString(root, MonitorApiContract.AgentVersionJsonKeys);

            if (string.IsNullOrWhiteSpace(contractVersion))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Missing API contract version");
                return EvaluateApiContractCompatibility(contractVersion, minClientContractVersion, reportedAgentVersion);
            }

            var result = EvaluateApiContractCompatibility(contractVersion, minClientContractVersion, reportedAgentVersion);
            activity?.SetTag("monitor.api_contract_version", contractVersion);
            activity?.SetTag("monitor.min_client_contract_version", minClientContractVersion);
            activity?.SetTag("monitor.agent_version", reportedAgentVersion);
            var statusCode = result.IsCompatible ? ActivityStatusCode.Ok : ActivityStatusCode.Error;
            var statusDescription = result.IsCompatible ? null : "API contract mismatch";
            activity?.SetStatus(statusCode, statusDescription);
            return result;
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
    }

    private void RecordUsageTelemetry(TimeSpan duration, bool success)
    {
        RecordTelemetry(duration, success, ref this._usageRequestCount, ref this._usageTotalLatencyMs, ref this._usageLastLatencyMs, ref this._usageErrorCount);
    }

    private void RecordRefreshTelemetry(TimeSpan duration, bool success)
    {
        RecordTelemetry(duration, success, ref this._refreshRequestCount, ref this._refreshTotalLatencyMs, ref this._refreshLastLatencyMs, ref this._refreshErrorCount);
    }

    private static void RecordTelemetry(
        TimeSpan duration,
        bool success,
        ref long requestCount,
        ref long totalLatencyMs,
        ref long lastLatencyMs,
        ref long errorCount)
    {
        var latencyMs = (long)Math.Max(0, duration.TotalMilliseconds);
        Interlocked.Increment(ref requestCount);
        Interlocked.Add(ref totalLatencyMs, latencyMs);
        Interlocked.Exchange(ref lastLatencyMs, latencyMs);
        if (!success)
        {
            Interlocked.Increment(ref errorCount);
        }
    }

    private static bool IsRecoverableUsageFailure(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException;
    }

    private static string DescribeUsageFailure(Exception ex)
    {
        return ex switch
        {
            TaskCanceledException => $"Request timed out after {UsageRequestTimeoutSeconds}s",
            HttpRequestException httpRequestException when !string.IsNullOrWhiteSpace(httpRequestException.Message) => httpRequestException.Message,
            _ => "Connection error",
        };
    }

    private static IReadOnlyList<string> GetActionableMetadataErrors(IReadOnlyList<string>? errors)
    {
        if (errors == null || errors.Count == 0)
        {
            return new List<string>();
        }

        return errors
            .Where(IsActionableMetadataError)
            .ToList();
    }

    private static bool IsActionableMetadataError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        if (error.StartsWith("Startup status:", StringComparison.OrdinalIgnoreCase))
        {
            return !error.Contains("running", StringComparison.OrdinalIgnoreCase);
        }

        return error.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("exception", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetJsonString(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }

    private static string? TryGetJsonString(JsonElement root, IEnumerable<string> propertyNames)
    {
        return propertyNames
            .Select(propertyName => TryGetJsonString(root, propertyName))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private string BuildMonitorUrl(string relativePath)
    {
        return $"{this.AgentUrl}{relativePath}";
    }

    public void InvalidateGroupedUsageCache()
    {
        lock (this._groupedUsageCacheLock)
        {
            this._cachedGroupedUsageSnapshot = null;
            this._cachedGroupedUsageETag = null;
        }
    }

    private void CacheGroupedUsageSnapshot(AgentGroupedUsageSnapshot snapshot, string? eTag)
    {
        lock (this._groupedUsageCacheLock)
        {
            this._cachedGroupedUsageSnapshot = snapshot;
            this._cachedGroupedUsageETag = eTag;
        }
    }

    private AgentGroupedUsageSnapshot? GetCachedGroupedUsageSnapshot()
    {
        lock (this._groupedUsageCacheLock)
        {
            return this._cachedGroupedUsageSnapshot;
        }
    }

    private string? GetCachedGroupedUsageETag()
    {
        lock (this._groupedUsageCacheLock)
        {
            return this._cachedGroupedUsageETag;
        }
    }

    private async Task<T?> GetFromMonitorJsonAsync<T>(string relativePath, string operationName, int? timeoutSeconds = null)
    {
        try
        {
            if (timeoutSeconds.HasValue)
            {
                using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds.Value));
                return await this._httpClient.GetFromJsonAsync<T>(
                    this.BuildMonitorUrl(relativePath),
                    this._jsonOptions,
                    requestTimeout.Token).ConfigureAwait(false);
            }

            return await this._httpClient.GetFromJsonAsync<T>(
                this.BuildMonitorUrl(relativePath),
                this._jsonOptions).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex)
        {
            this._logger?.LogWarning(
                ex,
                "{Operation} timeout after {TimeoutSeconds}s at {Url}",
                operationName,
                timeoutSeconds ?? 0,
                this.BuildMonitorUrl(relativePath));
            return default;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            this._logger?.LogWarning(ex, "{Operation} failed at {Url}", operationName, this.BuildMonitorUrl(relativePath));
            return default;
        }
    }

    private async Task<HttpResponseMessage?> SendMonitorRequestAsync(
        Func<HttpClient, Task<HttpResponseMessage>> requestFactory,
        string operationName)
    {
        try
        {
            return await requestFactory(this._httpClient).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            this._logger?.LogWarning(ex, "{Operation} failed against {AgentUrl}", operationName, this.AgentUrl);
            return null;
        }
    }

    private async Task<bool> SendMonitorStatusRequestAsync(
        Func<HttpClient, Task<HttpResponseMessage>> requestFactory,
        string operationName)
    {
        using var response = await this.SendMonitorRequestAsync(requestFactory, operationName).ConfigureAwait(false);
        return response?.IsSuccessStatusCode == true;
    }

    private async Task<T?> ReadMonitorResponseJsonAsync<T>(HttpResponseMessage response, string operationName)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(this._jsonOptions).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            this._logger?.LogWarning(
                ex,
                "{Operation} returned unreadable JSON from {AgentUrl} with status {StatusCode}",
                operationName,
                this.AgentUrl,
                (int)response.StatusCode);
            return default;
        }
    }

    private async Task<List<ProviderUsage>?> GetUsageOnceAsync()
    {
        using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(UsageRequestTimeoutSeconds));
        return await this._httpClient.GetFromJsonAsync<List<ProviderUsage>>(
            this.BuildMonitorUrl(MonitorApiRoutes.Usage),
            this._jsonOptions,
            requestTimeout.Token).ConfigureAwait(false);
    }

    private async Task<string> GetEndpointDetailsAsync(string endpointPath)
    {
        var response = await this.SendMonitorRequestAsync(
            httpClient => httpClient.GetAsync(this.BuildMonitorUrl(endpointPath)),
            nameof(this.GetEndpointDetailsAsync)).ConfigureAwait(false);
        if (response == null)
        {
            return "Request failed: no response from Monitor.";
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return $"HTTP {(int)response.StatusCode}: {body}";
        }

        return body;
    }
}
