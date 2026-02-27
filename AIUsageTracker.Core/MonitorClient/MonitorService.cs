using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public class MonitorService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public const string ExpectedApiContractVersion = "1";
    public string AgentUrl { get; set; } = "http://localhost:5000";

    public MonitorService() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
    }

    public List<string> LastAgentErrors { get; private set; } = new();
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

    public MonitorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
        
        // Discover actual port and errors from file
        _ = RefreshAgentInfoAsync();
    }

    public static void LogDiagnostic(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        lock (_diagnosticsLog)
        {
            _diagnosticsLog.Add($"[{timestamp}] {message}");
            if (_diagnosticsLog.Count > 100) _diagnosticsLog.RemoveAt(0);
        }
        System.Diagnostics.Debug.WriteLine($"[{timestamp}] [DIAG] {message}");
        Console.WriteLine($"[{timestamp}] [DIAG] {message}");
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
            RefreshErrorRatePercent = refreshRequestCount == 0 ? 0 : (refreshErrorCount / (double)refreshRequestCount) * 100.0
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
    
    public async Task RefreshAgentInfoAsync()
    {
        LogDiagnostic("Refreshing Agent Info from file...");
        try
        {
            var path = GetExistingAgentInfoPath();

            if (path != null)
            {
                var json = await File.ReadAllTextAsync(path);
                var info = JsonSerializer.Deserialize<MonitorInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (info != null)
                {
                    if (info.Port > 0) 
                    {
                        AgentUrl = $"http://localhost:{info.Port}";
                        LogDiagnostic($"Found Agent running on port {info.Port} from monitor.json");
                    }
                    LastAgentErrors = info.Errors ?? new List<string>();
                    return;
                }
            }
            
            LogDiagnostic("monitor.json not found or invalid, using default port 5000");
            LastAgentErrors = new List<string>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing agent info: {ex.Message}");
            AgentUrl = "http://localhost:5000";
            LastAgentErrors = new List<string>();
        }
    }

    private static string? GetExistingAgentInfoPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(appData, "AIUsageTracker", "monitor.json"),
            Path.Combine(appData, "AIConsumptionTracker", "monitor.json")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
    
    
    public async Task RefreshPortAsync()
    {
        await RefreshAgentInfoAsync();
    }

    // Provider usage endpoints
    public async Task<List<ProviderUsage>> GetUsageAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var usage = await _httpClient.GetFromJsonAsync<List<ProviderUsage>>(
                $"{AgentUrl}/api/usage", 
                _jsonOptions);
            LogDiagnostic($"Successfully fetched usage from {AgentUrl}");
            stopwatch.Stop();
            RecordUsageTelemetry(stopwatch.Elapsed, true);
            return usage ?? new List<ProviderUsage>();
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            RecordUsageTelemetry(stopwatch.Elapsed, false);
            LogDiagnostic($"Failed to fetch usage from {AgentUrl}: Connection error - {ex.Message}");
            throw; // Re-throw connection errors so caller knows Monitor is unreachable
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordUsageTelemetry(stopwatch.Elapsed, false);
            LogDiagnostic($"Failed to fetch usage from {AgentUrl}: {ex.Message}");
            return new List<ProviderUsage>();
        }
    }

    public async Task<ProviderUsage?> GetUsageByProviderAsync(string providerId)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ProviderUsage>(
                $"{AgentUrl}/api/usage/{providerId}",
                _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100)
    {
        try
        {
            var history = await _httpClient.GetFromJsonAsync<List<ProviderUsage>>(
                $"{AgentUrl}/api/history?limit={limit}",
                _jsonOptions);
            return history ?? new List<ProviderUsage>();
        }
        catch (Exception)
        {
            return new List<ProviderUsage>();
        }
    }

    public async Task<List<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100)
    {
        try
        {
            var history = await _httpClient.GetFromJsonAsync<List<ProviderUsage>>(
                $"{AgentUrl}/api/history/{providerId}?limit={limit}",
                _jsonOptions);
            return history ?? new List<ProviderUsage>();
        }
        catch (Exception)
        {
            return new List<ProviderUsage>();
        }
    }

    public async Task<bool> TriggerRefreshAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.PostAsync($"{AgentUrl}/api/refresh", null);
            stopwatch.Stop();
            RecordRefreshTelemetry(stopwatch.Elapsed, response.IsSuccessStatusCode);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            stopwatch.Stop();
            RecordRefreshTelemetry(stopwatch.Elapsed, false);
            return false;
        }
    }

    // Config endpoints
    public async Task<List<ProviderConfig>> GetConfigsAsync()
    {
        try
        {
            var configs = await _httpClient.GetFromJsonAsync<List<ProviderConfig>>(
                $"{AgentUrl}/api/config",
                _jsonOptions);
            return configs ?? new List<ProviderConfig>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetConfigsAsync error: {ex.Message}");
            return new List<ProviderConfig>();
        }
    }

    public async Task<bool> SaveConfigAsync(ProviderConfig config)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{AgentUrl}/api/config", 
                config, 
                _jsonOptions);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveConfigAsync error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveConfigAsync(string providerId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{AgentUrl}/api/config/{providerId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RemoveConfigAsync error: {ex.Message}");
            return false;
        }
    }

    // Preferences endpoints
    public async Task<AppPreferences> GetPreferencesAsync()
    {
        try
        {
            var prefs = await _httpClient.GetFromJsonAsync<AppPreferences>(
                $"{AgentUrl}/api/preferences",
                _jsonOptions);
            return prefs ?? new AppPreferences();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetPreferencesAsync error: {ex.Message}");
            return new AppPreferences();
        }
    }

    public async Task<bool> SavePreferencesAsync(AppPreferences preferences)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{AgentUrl}/api/preferences", 
                preferences, 
                _jsonOptions);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SavePreferencesAsync error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendTestNotificationAsync()
    {
        var result = await SendTestNotificationDetailedAsync();
        return result.Success;
    }

    public async Task<(bool Success, string Message)> SendTestNotificationDetailedAsync()
    {
        try
        {
            await RefreshPortAsync();
            var response = await _httpClient.PostAsync($"{AgentUrl}/api/notifications/test", null);

            if (response.IsSuccessStatusCode)
            {
                return (true, "Test sent. Check Windows notifications.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, "Monitor endpoint not available. Restart Monitor and try again.");
            }

            return (false, $"Monitor returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SendTestNotificationAsync error: {ex.Message}");
            return (false, "Could not reach Monitor. Ensure it is running and try again.");
        }
    }

    // Scan for keys endpoint
    public async Task<(int count, List<ProviderConfig> configs)> ScanForKeysAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync($"{AgentUrl}/api/scan-keys", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ScanKeysResponse>(_jsonOptions);
                if (result != null)
                {
                    return (result.Discovered, result.Configs ?? new List<ProviderConfig>());
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ScanForKeysAsync error: {ex.Message}");
        }
        return (0, new List<ProviderConfig>());
    }

    // Health check
    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{AgentUrl}/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> GetHealthDetailsAsync()
    {
        return GetEndpointDetailsAsync("/api/health");
    }

    public Task<string> GetDiagnosticsDetailsAsync()
    {
        return GetEndpointDetailsAsync("/api/diagnostics");
    }

    private async Task<string> GetEndpointDetailsAsync(string endpointPath)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{AgentUrl}{endpointPath}");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"HTTP {(int)response.StatusCode}: {body}";
            }

            return body;
        }
        catch (Exception ex)
        {
            return $"Request failed: {ex.Message}";
        }
    }

    public async Task<AgentContractHandshakeResult> CheckApiContractAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{AgentUrl}/api/health");
            if (!response.IsSuccessStatusCode)
            {
                return new AgentContractHandshakeResult
                {
                    IsReachable = false,
                    IsCompatible = false,
                    Message = $"Agent health check failed ({(int)response.StatusCode})."
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
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
                    Message = $"Agent API contract version is missing (expected {ExpectedApiContractVersion})."
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
                    Message = "Agent API contract is compatible."
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
                Message = $"Agent API contract mismatch: expected {ExpectedApiContractVersion}, got {contractVersion}{versionSuffix}."
            };
        }
        catch (Exception ex)
        {
            return new AgentContractHandshakeResult
            {
                IsReachable = false,
                IsCompatible = false,
                Message = $"Agent API handshake failed: {ex.Message}"
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
            _ => null
        };
    }

    private class ScanKeysResponse
    {
        [JsonPropertyName("discovered")]
        public int Discovered { get; set; }
        
        [JsonPropertyName("configs")]
        public List<ProviderConfig>? Configs { get; set; }
    }

    // Diagnostics & Export
    public async Task<(bool success, string message)> CheckProviderAsync(string providerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{AgentUrl}/api/providers/{providerId}/check");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CheckResponse>(_jsonOptions);
                return (result?.Success ?? false, result?.Message ?? "Unknown status");
            }
            else
            {
                // Try to read error message if available
                try 
                {
                    var error = await response.Content.ReadFromJsonAsync<CheckResponse>(_jsonOptions);
                    if (!string.IsNullOrEmpty(error?.Message))
                        return (false, error.Message);
                }
                catch { }
                
                return (false, $"HTTP {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }

    public async Task<Stream?> ExportDataAsync(string format, int days)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{AgentUrl}/api/export?format={format}&days={days}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExportDataAsync error: {ex.Message}");
        }
        return null; // or throw
    }

    private class CheckResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

public sealed class AgentTelemetrySnapshot
{
    public long UsageRequestCount { get; init; }
    public long UsageErrorCount { get; init; }
    public double UsageAverageLatencyMs { get; init; }
    public long UsageLastLatencyMs { get; init; }
    public double UsageErrorRatePercent { get; init; }
    public long RefreshRequestCount { get; init; }
    public long RefreshErrorCount { get; init; }
    public double RefreshAverageLatencyMs { get; init; }
    public long RefreshLastLatencyMs { get; init; }
    public double RefreshErrorRatePercent { get; init; }
}

public sealed class AgentContractHandshakeResult
{
    public bool IsReachable { get; init; }
    public bool IsCompatible { get; init; }
    public string? AgentContractVersion { get; init; }
    public string? AgentVersion { get; init; }
    public string Message { get; init; } = string.Empty;
}

