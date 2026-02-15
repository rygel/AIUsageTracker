using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIConsumptionTracker.UI.Slim.Models;

namespace AIConsumptionTracker.UI.Slim.Services;

public class AgentService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public string AgentUrl { get; set; } = "http://localhost:5000";

    public AgentService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
        
        // Discover actual port from file
        _ = DiscoverPortAsync();
    }
    
    private async Task DiscoverPortAsync()
    {
        var port = await GetAgentPortAsync();
        AgentUrl = $"http://localhost:{port}";
    }
    
    private static async Task<int> GetAgentPortAsync()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var portFile = Path.Combine(appData, "AIConsumptionTracker", "Agent", "agent.port");
            
            if (File.Exists(portFile))
            {
                var portStr = await File.ReadAllTextAsync(portFile);
                if (int.TryParse(portStr, out int port))
                {
                    return port;
                }
            }
            
            return 5000;
        }
        catch
        {
            return 5000;
        }
    }
    
    public async Task RefreshPortAsync()
    {
        await DiscoverPortAsync();
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
}
