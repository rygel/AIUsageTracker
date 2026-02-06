using System.Net.Http.Json;
using System.Text.Json;
using AIConsumptionTracker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Infrastructure.Services;

public class GitHubAuthService : IGitHubAuthService
{
    // Using common Client ID for Copilot integrations (VS Code's ID) as this is required to get the 'copilot' scope permissions correctly.
    // In a real production app for general GitHub access, we would register our own.
    private const string CLIENT_ID = "Iv1.b507a08c87ecfe98"; 
    private const string AUTH_URL = "https://github.com/login/device/code";
    private const string TOKEN_URL = "https://github.com/login/oauth/access_token";
    private const string SCOPE = "read:user copilot"; // Requesting copilot scope

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubAuthService> _logger;
    private string? _currentToken;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_currentToken);

    public GitHubAuthService(HttpClient httpClient, ILogger<GitHubAuthService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(string deviceCode, string userCode, string verificationUri, int expiresIn, int interval)> InitiateDeviceFlowAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, AUTH_URL);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", CLIENT_ID),
                new KeyValuePair<string, string>("scope", SCOPE)
            });
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<DeviceFlowResponse>();
            if (result == null) throw new Exception("Failed to parse device flow response.");

            return (result.device_code, result.user_code, result.verification_uri, result.expires_in, result.interval);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating device flow");
            throw;
        }
    }

    public async Task<string?> PollForTokenAsync(string deviceCode, int interval)
    {
        // Polling logic would typically be handled by the caller or a loop here. 
        // For this method, we make a SINGLE check. The caller (UI) should loop.
        
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, TOKEN_URL);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", CLIENT_ID),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            });
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var code = error.GetString();
                if (code == "authorization_pending") return null; // Keep polling
                if (code == "slow_down") return "SLOW_DOWN"; // Signal to slow down
                if (code == "expired_token") throw new Exception("Token expired");
                if (code == "access_denied") throw new Exception("Access denied");
            }

            if (root.TryGetProperty("access_token", out var tokenProp))
            {
                _currentToken = tokenProp.GetString();
                return _currentToken;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling for token");
            return null;
        }
    }

    public Task<string?> RefreshTokenAsync(string refreshToken)
    {
        // Device flow tokens for apps like VS Code usually last a long time or don't use refresh tokens in the same way 
        // as web apps (they use the access token until invalid). 
        // Implementing placeholder.
        return Task.FromResult<string?>(null);
    }

    public string? GetCurrentToken() => _currentToken;

    public void Logout()
    {
        _currentToken = null;
    }

    public void InitializeToken(string token)
    {
        _currentToken = token;
    }

    // Helper class for JSON deserialization
    private class DeviceFlowResponse
    {
        public string device_code { get; set; } = "";
        public string user_code { get; set; } = "";
        public string verification_uri { get; set; } = "";
        public int expires_in { get; set; }
        public int interval { get; set; }
    }
}
