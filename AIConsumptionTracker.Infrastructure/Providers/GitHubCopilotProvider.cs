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
        string description = isAvailable ? "Authenticated via GitHub" : "Not authenticated. Please login in Settings.";
        string username = "User";
        string planName = "Unknown Plan";
        DateTime? resetTime = null;
        double percentage = 0;
        double costUsed = 0;
        double costLimit = 0;

        // Real usage tracking would require calling GitHub's API here.
        // For now, we confirm we have a token.
        
        if (isAvailable)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AIConsumptionTracker", "1.0"));

                var response = await _httpClient.SendAsync(request);
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
                         // This is what VS Code uses to verify subscription status
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
                                     
                                     // Check for expiration
                                     if (internalDoc.RootElement.TryGetProperty("expires_at", out var expProp))
                                     {
                                          // Token expiry, ignore for display
                                     }
                                     
                                     // Check for limits/usage in the internal token
                                     // This is often where specific volume constraints are hidden
                                     if (internalDoc.RootElement.TryGetProperty("limits", out var limitsProp))
                                     {
                                          description += $"\nLimits: {limitsProp.ToString()}"; 
                                          // If we can parse specific keys like 'max_requests' or 'current_usage', do so here
                                     }

                                     description = $"Authenticated as {username} ({planName})";
                                 }
                             }
                             else
                             {
                                 description = $"Authenticated as {username} (Plan info unavailable)";
                             }
                             
                             // Try to fetch wider billing usage if possible (experimental)
                             try 
                             {
                                 var usageRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/usage");
                                 usageRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                                 usageRequest.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AIConsumptionTracker", "1.0"));
                                 
                                 var usageResponse = await _httpClient.SendAsync(usageRequest);
                                 if (usageResponse.IsSuccessStatusCode)
                                 {
                                     var usageJson = await usageResponse.Content.ReadAsStringAsync();
                                     // Parse for any relevant "percentage" or "limit" fields 
                                     // This is a blind probe based on user report of "7.3%"
                                     description += "\n(Billing Usage data found)";
                                 }
                             }
                             catch {}

                         }
                         catch 
                         {
                             description = $"Authenticated as {username}";
                         }
                     }
                }
                
                // Fetch rate limits to show "actual usage" of the API
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
                                
                                // Append rate limit info
                                if (description.Contains("Plan"))
                                {
                                     description += $"\nAPI Quota: {used}/{limit} used ({remaining} remaining)";
                                }
                                else
                                {
                                     description += $" (API Quota: {used}/{limit})";
                                }
                                
                                // Map to Cost/Limit fields for bar graph
                                costLimit = limit;
                                costUsed = used;
                                percentage = limit > 0 ? ((double)used / limit) * 100 : 0;
                                
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
                     description = "Authenticated (Offline or API Error)";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch GitHub profile");
                description = "Authenticated (Error fetching details)";
            }
        }

        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "GitHub Copilot",
            AccountName = username, // Move username to AccountName column
            IsAvailable = isAvailable,
            Description = isAvailable 
                ? $"API Rate Limit (Hourly): {costUsed}/{costLimit} Used" 
                : description,
            UsagePercentage = percentage, 
            CostLimit = costLimit,
            CostUsed = costUsed,
            UsageUnit = "Reqs",
            PaymentType = PaymentType.Quota,
            IsQuotaBased = true,
            AuthSource = planName, // Show plan in tooltip/metadata
            NextResetTime = resetTime
        }};
    }
}
