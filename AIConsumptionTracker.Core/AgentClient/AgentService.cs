using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Core.AgentClient;

public class AgentService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public string AgentUrl { get; set; } = "http://localhost:5000";

    public AgentService() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
    }

    public List<string> LastAgentErrors { get; private set; } = new();

    public AgentService(HttpClient httpClient)
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
    
    public async Task RefreshAgentInfoAsync()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var agentDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");
            var jsonFile = Path.Combine(agentDir, "agent.json");
            var infoFile = Path.Combine(agentDir, "agent.info");
            
            string? path = null;
            if (File.Exists(jsonFile)) path = jsonFile;
            else if (File.Exists(infoFile)) path = infoFile;

            if (path != null)
            {
                var json = await File.ReadAllTextAsync(path);
                var info = JsonSerializer.Deserialize<AgentInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (info != null)
                {
                    if (info.Port > 0) AgentUrl = $"http://localhost:{info.Port}";
                    LastAgentErrors = info.Errors ?? new List<string>();
                    return;
                }
            }
            
            AgentUrl = "http://localhost:5000";
            LastAgentErrors = new List<string>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing agent info: {ex.Message}");
            AgentUrl = "http://localhost:5000";
            LastAgentErrors = new List<string>();
        }
    }
    
    private class AgentInfo
    {
        public int Port { get; set; }
        public string? StartedAt { get; set; }
        public int ProcessId { get; set; }
        public bool DebugMode { get; set; }
        public List<string>? Errors { get; set; }
    }
    
    public async Task RefreshPortAsync()
    {
        await RefreshAgentInfoAsync();
    }

    // Provider usage endpoints
    public async Task<List<ProviderUsage>> GetUsageAsync()
    {
        try
        {
            var usage = await _httpClient.GetFromJsonAsync<List<ProviderUsage>>(
                $"{AgentUrl}/api/usage", 
                _jsonOptions);
            return usage ?? new List<ProviderUsage>();
        }
        catch (Exception)
        {
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
        try
        {
            var response = await _httpClient.PostAsync($"{AgentUrl}/api/refresh", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
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
