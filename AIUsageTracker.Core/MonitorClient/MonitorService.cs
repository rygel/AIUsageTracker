// <copyright file="MonitorService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient
{
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Models;
    using Microsoft.Extensions.Logging;

    public class MonitorService : IMonitorService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ILogger<MonitorService>? _logger;
        private const int UsageRequestTimeoutSeconds = 8;
        private const int ConfigRequestTimeoutSeconds = 3;

        public const string ExpectedApiContractVersion = "1";

        public string AgentUrl { get; set; } = "http://localhost:5000";

        private static HttpClient? _sharedHttpClient;

        public MonitorService() : this(GetOrCreateHttpClient(), null)
        {
        }

        private static HttpClient GetOrCreateHttpClient()
        {
            if (_sharedHttpClient == null)
            {
                _sharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            }

            return _sharedHttpClient;
        }

        public MonitorService(HttpClient httpClient, ILogger<MonitorService>? logger)
        {
            this._httpClient = httpClient;
            this._logger = logger;
            this._jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
            };

            // Note: Port discovery is now done explicitly via RefreshPortAsync()
            // to avoid race conditions where the Monitor port changes
        }

        public IReadOnlyList<string> LastAgentErrors { get; private set; } = new List<string>();

        private static readonly List<string> _diagnosticsLog = new();

        public static IReadOnlyList<string> DiagnosticsLog => _diagnosticsLog;

        private static long _usageRequestCount;
        private static long _usageErrorCount;
        private static long _usageTotalLatencyMs;
        private static long _usageLastLatencyMs;
        private static long _refreshRequestCount;
        private static long _refreshErrorCount;
        private static long _refreshTotalLatencyMs;
        private static long _refreshLastLatencyMs;

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

        public static AgentTelemetrySnapshot GetTelemetrySnapshot()
        {
            var usageRequestCount = Interlocked.Read(ref _usageRequestCount);
            var usageErrorCount = Interlocked.Read(ref _usageErrorCount);
            var usageTotalLatencyMs = Interlocked.Read(ref _usageTotalLatencyMs);
            var usageLastLatencyMs = Interlocked.Read(ref _usageLastLatencyMs);
            var refreshRequestCount = Interlocked.Read(ref _refreshRequestCount);
            var refreshErrorCount = Interlocked.Read(ref _refreshErrorCount);
            var refreshTotalLatencyMs = Interlocked.Read(ref _refreshTotalLatencyMs);
            var refreshLastLatencyMs = Interlocked.Read(ref _refreshLastLatencyMs);

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

        private static void RecordUsageTelemetry(TimeSpan duration, bool success)
        {
            var latencyMs = (long)Math.Max(0, duration.TotalMilliseconds);
            Interlocked.Increment(ref _usageRequestCount);
            Interlocked.Add(ref _usageTotalLatencyMs, latencyMs);
            Interlocked.Exchange(ref _usageLastLatencyMs, latencyMs);
            if (!success)
            {
                Interlocked.Increment(ref _usageErrorCount);
            }
        }

        private static void RecordRefreshTelemetry(TimeSpan duration, bool success)
        {
            var latencyMs = (long)Math.Max(0, duration.TotalMilliseconds);
            Interlocked.Increment(ref _refreshRequestCount);
            Interlocked.Add(ref _refreshTotalLatencyMs, latencyMs);
            Interlocked.Exchange(ref _refreshLastLatencyMs, latencyMs);
            if (!success)
            {
                Interlocked.Increment(ref _refreshErrorCount);
            }
        }

        private string BuildMonitorUrl(string relativePath)
        {
            return $"{this.AgentUrl}{relativePath}";
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
            catch (Exception ex)
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
            catch (Exception ex)
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
            catch (Exception ex)
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

        public async Task RefreshAgentInfoAsync()
        {
            LogDiagnostic("Refreshing Monitor Info from file...");
            try
            {
                var info = await MonitorLauncher.GetAndValidateMonitorInfoAsync().ConfigureAwait(false);
                if (info != null)
                {
                    if (info.Port > 0)
                    {
                        this.AgentUrl = $"http://localhost:{info.Port}";
                        LogDiagnostic($"Found Monitor running on port {info.Port} from monitor.json");
                    }

                    this.LastAgentErrors = info.Errors ?? new List<string>();
                    return;
                }

                LogDiagnostic("monitor.json missing, stale, or invalid; using default port 5000");
                this.AgentUrl = "http://localhost:5000";
                this.LastAgentErrors = new List<string>();
            }
            catch (Exception ex)
            {
                this._logger?.LogWarning(ex, "Error refreshing monitor info");
                this.AgentUrl = "http://localhost:5000";
                this.LastAgentErrors = new List<string>();
            }
        }

        public async Task RefreshPortAsync()
        {
            var (isRunning, port) = await MonitorLauncher.IsAgentRunningWithPortAsync().ConfigureAwait(false);
            if (!isRunning)
            {
                MonitorService.LogDiagnostic($"Monitor not responding on port {port}. Attempting to locate...");
            }

            this.AgentUrl = $"http://localhost:{port}";
        }

        // Provider usage endpoints
        public async Task<IReadOnlyList<ProviderUsage>> GetUsageAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(UsageRequestTimeoutSeconds));
                var usage = await this._httpClient.GetFromJsonAsync<List<ProviderUsage>>(
                    $"{this.AgentUrl}/api/usage",
                    this._jsonOptions,
                    requestTimeout.Token).ConfigureAwait(false);
                LogDiagnostic($"Successfully fetched usage from {this.AgentUrl}");
                stopwatch.Stop();
                RecordUsageTelemetry(stopwatch.Elapsed, true);
                return usage ?? new List<ProviderUsage>();
            }
            catch (HttpRequestException)
            {
                // Connection failed - Monitor may have moved to a different port
                // Refresh port discovery and retry once
                LogDiagnostic($"Connection failed to {this.AgentUrl}, refreshing port and retrying...");
                await this.RefreshPortAsync().ConfigureAwait(false);

                try
                {
                    using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(UsageRequestTimeoutSeconds));
                    var usage = await this._httpClient.GetFromJsonAsync<List<ProviderUsage>>(
                        $"{this.AgentUrl}/api/usage",
                        this._jsonOptions,
                        requestTimeout.Token).ConfigureAwait(false);
                    LogDiagnostic($"Successfully fetched usage from {this.AgentUrl} after port refresh");
                    stopwatch.Stop();
                    RecordUsageTelemetry(stopwatch.Elapsed, true);
                    return usage ?? new List<ProviderUsage>();
                }
                catch (HttpRequestException)
                {
                    stopwatch.Stop();
                    RecordUsageTelemetry(stopwatch.Elapsed, false);
                    LogDiagnostic($"Failed to fetch usage from {this.AgentUrl} after port refresh: Connection error");
                    return new List<ProviderUsage>();
                }
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                RecordUsageTelemetry(stopwatch.Elapsed, false);
                LogDiagnostic($"Failed to fetch usage from {this.AgentUrl}: request timed out after {UsageRequestTimeoutSeconds}s");
                return new List<ProviderUsage>();
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordUsageTelemetry(stopwatch.Elapsed, false);
                LogDiagnostic($"Failed to fetch usage from {this.AgentUrl}: {ex.Message}");
                return new List<ProviderUsage>();
            }
        }

        public async Task<ProviderUsage?> GetUsageByProviderAsync(string providerId)
        {
            return await this.GetFromMonitorJsonAsync<ProviderUsage>(
                $"/api/usage/{providerId}",
                nameof(this.GetUsageByProviderAsync)).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<ProviderUsage>> GetHistoryAsync(int limit = 100)
        {
            var history = await this.GetFromMonitorJsonAsync<List<ProviderUsage>>(
                $"/api/history?limit={limit}",
                nameof(this.GetHistoryAsync)).ConfigureAwait(false);
            return history ?? new List<ProviderUsage>();
        }

        public async Task<IReadOnlyList<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100)
        {
            var history = await this.GetFromMonitorJsonAsync<List<ProviderUsage>>(
                $"/api/history/{providerId}?limit={limit}",
                nameof(this.GetHistoryByProviderAsync)).ConfigureAwait(false);
            return history ?? new List<ProviderUsage>();
        }

        public async Task<bool> TriggerRefreshAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await this.SendMonitorRequestAsync(
                httpClient => httpClient.PostAsync(this.BuildMonitorUrl("/api/refresh"), null),
                nameof(this.TriggerRefreshAsync)).ConfigureAwait(false);

            stopwatch.Stop();
            if (response != null)
            {
                RecordRefreshTelemetry(stopwatch.Elapsed, response.IsSuccessStatusCode);
                return response.IsSuccessStatusCode;
            }

            RecordRefreshTelemetry(stopwatch.Elapsed, false);
            return false;
        }

        // Config endpoints
        public async Task<IReadOnlyList<ProviderConfig>> GetConfigsAsync()
        {
            var configs = await this.GetFromMonitorJsonAsync<List<ProviderConfig>>(
                "/api/config",
                nameof(this.GetConfigsAsync),
                ConfigRequestTimeoutSeconds).ConfigureAwait(false);
            return configs ?? new List<ProviderConfig>();
        }

        public async Task<bool> SaveConfigAsync(ProviderConfig config)
        {
            return await this.SendMonitorStatusRequestAsync(
                httpClient => httpClient.PostAsJsonAsync(
                    this.BuildMonitorUrl("/api/config"),
                    config,
                    this._jsonOptions),
                nameof(this.SaveConfigAsync)).ConfigureAwait(false);
        }

        public async Task<bool> RemoveConfigAsync(string providerId)
        {
            return await this.SendMonitorStatusRequestAsync(
                httpClient => httpClient.DeleteAsync(this.BuildMonitorUrl($"/api/config/{providerId}")),
                nameof(this.RemoveConfigAsync)).ConfigureAwait(false);
        }

        // Preferences endpoints
        public async Task<AppPreferences> GetPreferencesAsync()
        {
            var prefs = await this.GetFromMonitorJsonAsync<AppPreferences>(
                "/api/preferences",
                nameof(this.GetPreferencesAsync)).ConfigureAwait(false);
            return prefs ?? new AppPreferences();
        }

        public async Task<bool> SavePreferencesAsync(AppPreferences preferences)
        {
            return await this.SendMonitorStatusRequestAsync(
                httpClient => httpClient.PostAsJsonAsync(
                    this.BuildMonitorUrl("/api/preferences"),
                    preferences,
                    this._jsonOptions),
                nameof(this.SavePreferencesAsync)).ConfigureAwait(false);
        }

        public async Task<bool> SendTestNotificationAsync()
        {
            var result = await this.SendTestNotificationDetailedAsync().ConfigureAwait(false);
            return result.Success;
        }

        public async Task<AgentTestNotificationResult> SendTestNotificationDetailedAsync()
        {
            try
            {
                await this.RefreshPortAsync().ConfigureAwait(false);
                using var response = await this._httpClient.PostAsync(this.BuildMonitorUrl("/api/notifications/test"), null).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var result = await this.ReadMonitorResponseJsonAsync<AgentTestNotificationResult>(
                        response,
                        nameof(this.SendTestNotificationDetailedAsync)).ConfigureAwait(false);
                    return result ?? new AgentTestNotificationResult
                    {
                        Success = true,
                        Message = "Test sent. Check system notifications.",
                    };
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new AgentTestNotificationResult
                    {
                        Success = false,
                        Message = "Monitor endpoint not available. Restart Monitor and try again.",
                    };
                }

                return new AgentTestNotificationResult
                {
                    Success = false,
                    Message = $"Monitor returned {(int)response.StatusCode} ({response.ReasonPhrase}).",
                };
            }
            catch (Exception ex)
            {
                this._logger?.LogWarning(ex, "SendTestNotificationAsync error");
                return new AgentTestNotificationResult
                {
                    Success = false,
                    Message = "Could not reach Monitor. Ensure it is running and try again.",
                };
            }
        }

        // Scan for keys endpoint
        public async Task<(int Count, IReadOnlyList<ProviderConfig> Configs)> ScanForKeysAsync()
        {
            using var response = await this.SendMonitorRequestAsync(
                httpClient => httpClient.PostAsync(this.BuildMonitorUrl("/api/scan-keys"), null),
                nameof(this.ScanForKeysAsync)).ConfigureAwait(false);
            if (response?.IsSuccessStatusCode == true)
            {
                var result = await this.ReadMonitorResponseJsonAsync<ScanKeysResponse>(
                    response,
                    nameof(this.ScanForKeysAsync)).ConfigureAwait(false);
                if (result != null)
                {
                    return (result.Discovered, result.Configs ?? new List<ProviderConfig>());
                }
            }

            return (0, new List<ProviderConfig>());
        }

        // Health check
        public async Task<bool> CheckHealthAsync()
        {
            var response = await this.SendMonitorRequestAsync(
                httpClient => httpClient.GetAsync(this.BuildMonitorUrl("/api/health")),
                nameof(this.CheckHealthAsync)).ConfigureAwait(false);
            return response?.IsSuccessStatusCode == true;
        }

        public Task<string> GetHealthDetailsAsync()
        {
            return this.GetEndpointDetailsAsync("/api/health");
        }

        public Task<string> GetDiagnosticsDetailsAsync()
        {
            return this.GetEndpointDetailsAsync("/api/diagnostics");
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

        public async Task<AgentContractHandshakeResult> CheckApiContractAsync()
        {
            try
            {
                using var response = await this._httpClient.GetAsync(this.BuildMonitorUrl("/api/health")).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new AgentContractHandshakeResult
                    {
                        IsReachable = false,
                        IsCompatible = false,
                        Message = $"Agent health check failed ({(int)response.StatusCode}).",
                    };
                }

                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                var root = document.RootElement;

                var contractVersion =
                    TryGetJsonString(root, "apiContractVersion") ??
                    TryGetJsonString(root, "api_contract_version");

                var reportedAgentVersion =
                    TryGetJsonString(root, "agentVersion") ??
                    TryGetJsonString(root, "agent_version") ??
                    TryGetJsonString(root, "version");

                if (string.IsNullOrWhiteSpace(contractVersion))
                {
                    return new AgentContractHandshakeResult
                    {
                        IsReachable = true,
                        IsCompatible = false,
                        AgentVersion = reportedAgentVersion,
                        Message = $"Agent API contract version is missing (expected {ExpectedApiContractVersion}).",
                    };
                }

                if (string.Equals(contractVersion, ExpectedApiContractVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return new AgentContractHandshakeResult
                    {
                        IsReachable = true,
                        IsCompatible = true,
                        AgentContractVersion = contractVersion,
                        AgentVersion = reportedAgentVersion,
                        Message = "Agent API contract is compatible.",
                    };
                }

                var versionSuffix = string.IsNullOrWhiteSpace(reportedAgentVersion)
                    ? string.Empty
                    : $" (agent {reportedAgentVersion})";

                return new AgentContractHandshakeResult
                {
                    IsReachable = true,
                    IsCompatible = false,
                    AgentContractVersion = contractVersion,
                    AgentVersion = reportedAgentVersion,
                    Message = $"Agent API contract mismatch: expected {ExpectedApiContractVersion}, got {contractVersion}{versionSuffix}.",
                };
            }
            catch (Exception ex)
            {
                this._logger?.LogWarning(ex, "CheckApiContractAsync failed against {AgentUrl}", this.AgentUrl);
                return new AgentContractHandshakeResult
                {
                    IsReachable = false,
                    IsCompatible = false,
                    Message = $"Agent API handshake failed: {ex.Message}",
                };
            }
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

        private class ScanKeysResponse
        {
            [JsonPropertyName("discovered")]
            public int Discovered { get; set; }

            [JsonPropertyName("configs")]
            public IReadOnlyList<ProviderConfig>? Configs { get; set; }
        }

        // Diagnostics & Export
        public async Task<(bool Success, string Message)> CheckProviderAsync(string providerId)
        {
            try
            {
                using var response = await this._httpClient.GetAsync(this.BuildMonitorUrl($"/api/providers/{providerId}/check")).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var result = await this.ReadMonitorResponseJsonAsync<CheckResponse>(
                        response,
                        nameof(this.CheckProviderAsync)).ConfigureAwait(false);
                    return (result?.Success ?? false, result?.Message ?? "Unknown status");
                }

                // Try to read error message if available
                var error = await this.ReadMonitorResponseJsonAsync<CheckResponse>(
                    response,
                    nameof(this.CheckProviderAsync)).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(error?.Message))
                {
                    return (false, error.Message);
                }

                return (false, $"HTTP {response.StatusCode}");
            }
            catch (Exception ex)
            {
                this._logger?.LogWarning(ex, "CheckProviderAsync failed for {ProviderId}", providerId);
                return (false, $"Connection error: {ex.Message}");
            }
        }

        public async Task<string> ExportDataAsync(string format)
        {
            using var response = await this.SendMonitorRequestAsync(
                httpClient => httpClient.GetAsync(this.BuildMonitorUrl($"/api/export/{format}")),
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
                httpClient => httpClient.GetAsync(this.BuildMonitorUrl($"/api/export?format={format}&days={days}")),
                nameof(this.ExportDataAsync)).ConfigureAwait(false);
            if (response?.IsSuccessStatusCode == true)
            {
                return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            }

            return null;
        }

        private class CheckResponse
        {
            public bool Success { get; set; }

            public string Message { get; set; } = string.Empty;
        }
    }
}
