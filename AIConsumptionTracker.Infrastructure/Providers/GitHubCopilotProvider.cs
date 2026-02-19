using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class GitHubCopilotProvider : IProviderService
{
    public string ProviderId => "github-copilot";
    private readonly IGitHubAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubCopilotProvider> _logger;

    public GitHubCopilotProvider(HttpClient httpClient, ILogger<GitHubCopilotProvider> logger, IGitHubAuthService authService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _authService = authService;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        var token = _authService.GetCurrentToken();
        
        if (string.IsNullOrEmpty(token))
        {
            // If explicit API key is provided in config (e.g. from environment or manual entry), use that.
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                token = config.ApiKey;
            }
        }

        bool isAvailable = !string.IsNullOrEmpty(token);
        if (!isAvailable)
        {
             return new[] { new ProviderUsage
             {
                 ProviderId = ProviderId,
                 ProviderName = "GitHub Copilot",
                 IsAvailable = false,
                 Description = "Not authenticated. Please login in Settings.",
                 IsQuotaBased = true,
                 PlanType = PlanType.Coding
             }};
        }

        string description = "Authenticated";
        string username = "User";
        string planName = "";
        DateTime? resetTime = null;
        double percentage = 0;
        double costUsed = 0;
        double costLimit = 0;
        bool hasRateLimitData = false;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AIConsumptionTracker", "1.0"));

            var response = await _httpClient.SendAsync(request);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "GitHub Copilot",
                    IsAvailable = false,
                    Description = "Authentication failed (401). Please re-login.",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding
                }};
            }

            if (response.IsSuccessStatusCode)
            {
                 var json = await response.Content.ReadAsStringAsync();
                 using (var doc = System.Text.Json.JsonDocument.Parse(json))
                 {
                     if (doc.RootElement.TryGetProperty("login", out var loginElement))
                     {
                         username = loginElement.GetString() ?? "User";
                     }
                     
                     // Try to get Copilot specific plan info via internal token
                     try 
                     {
                         var internalRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/v2/token");
                         internalRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                         internalRequest.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AIConsumptionTracker", "1.0"));
                         
                         var internalResponse = await _httpClient.SendAsync(internalRequest);
                         if (internalResponse.IsSuccessStatusCode)
                         {
                             var internalJson = await internalResponse.Content.ReadAsStringAsync();
                             using (var internalDoc = System.Text.Json.JsonDocument.Parse(internalJson))
                             {
                                 string sku = "";
                                 if (internalDoc.RootElement.TryGetProperty("sku", out var skuProp))
                                     sku = skuProp.GetString() ?? "";
                                     
                                 // "copilot_individual", "copilot_business", etc.
                                 planName = sku switch 
                                 {
                                     "copilot_individual" => "Copilot Individual",
                                     "copilot_business" => "Copilot Business",
                                     "copilot_enterprise" => "Copilot Enterprise",
                                     _ => sku
                                 };
                             }
                             description = $"Authenticated as {username} ({planName})";
                         }
                         else 
                         {
                             description = $"Authenticated as {username}";
                         }
                     }
                     catch 
                     {
                         description = $"Authenticated as {username}";
                     }
                 }
            }
            
            // Fetch rate limits to show usage
            try 
            {
                var rateRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/rate_limit");
                rateRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                rateRequest.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AIConsumptionTracker", "1.0"));
                
                var rateResponse = await _httpClient.SendAsync(rateRequest);
                if (rateResponse.IsSuccessStatusCode)
                {
                    var rateJson = await rateResponse.Content.ReadAsStringAsync();
                    using (var rateDoc = System.Text.Json.JsonDocument.Parse(rateJson))
                    {
                        if (rateDoc.RootElement.TryGetProperty("resources", out var res) && 
                            res.TryGetProperty("core", out var core))
                        {
                            int limit = core.GetProperty("limit").GetInt32();
                            int remaining = core.GetProperty("remaining").GetInt32();
                            int used = limit - remaining;
                            
                            hasRateLimitData = true;
                            
                            // Show REMAINING percentage (like quota providers)
                            costLimit = limit;
                            costUsed = used;
                            percentage = limit > 0 ? ((double)remaining / limit) * 100 : 100;  // REMAINING %
                            
                            if (core.TryGetProperty("reset", out var resetProp))
                            {
                                var unixTime = resetProp.GetInt64();
                                resetTime = DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime;
                            }
                        }
                    }
                }
            }
            catch {} // Ignore rate limit fetch errors

            if (!response.IsSuccessStatusCode)
            {
                 description = $"Error: {response.StatusCode}";
                 isAvailable = false;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching GitHub profile");
            description = "Network Error: Unable to reach GitHub";
            isAvailable = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch GitHub profile");
            description = $"Error: {ex.Message}";
            isAvailable = false;
        }

        string finalDescription;
        if (hasRateLimitData)
        {
             finalDescription = $"API Rate Limit: {costLimit - costUsed}/{costLimit} Remaining";
             if (!string.IsNullOrEmpty(planName))
             {
                 finalDescription += $" ({planName})";
             }
        }
        else
        {
             finalDescription = description;
        }

        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "GitHub Copilot",
            AccountName = username, 
            IsAvailable = isAvailable,
            Description = finalDescription,
            RequestsPercentage = percentage,  // REMAINING % for quota-like behavior
            RequestsAvailable = costLimit,
            RequestsUsed = costUsed,
            UsageUnit = "Requests",
            PlanType = PlanType.Coding, 
            IsQuotaBased = true,
            AuthSource = string.IsNullOrEmpty(planName) ? "Unknown" : planName,
            NextResetTime = resetTime
        }};
    }
}
