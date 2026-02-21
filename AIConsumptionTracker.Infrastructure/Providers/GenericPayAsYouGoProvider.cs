using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

// =============================================================================
// ⚠️  AI ASSISTANTS: CALCULATION LOGIC WARNING - SEE LINE ~276
// =============================================================================
// Provider calculation follows strict design rules documented in DESIGN.md
// Quota providers MUST return REMAINING percentage
// DO NOT modify utilization calculation without explicit developer approval
// =============================================================================

namespace AIConsumptionTracker.Infrastructure.Providers;

public class GenericPayAsYouGoProvider : IProviderService
{
    public virtual string ProviderId => "generic-pay-as-you-go";
    protected readonly HttpClient _httpClient;
    protected readonly ILogger _logger;

    public GenericPayAsYouGoProvider(HttpClient httpClient, ILogger<GenericPayAsYouGoProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    protected GenericPayAsYouGoProvider(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public virtual async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ArgumentException("API Key not found.");
        }



        // Use a default OpenCode-like URL if none provided? 
        // Or expect it in a specific field? ProviderConfig doesn't have a Url field yet.
        // Let's assume we can use the provider_id as a hint or just use a standard one.
        // For now, let's allow it to handle things like "mistral", "synthetic" if they were pay-as-you-go.
        
        // Actually, the user might want to configure the URL in a way we haven't defined.
        // Let's check if we should add a 'url' to ProviderConfig or hijack another field.
        // For now, let's implement the logic for a standard credits endpoint.

        var url = config.BaseUrl;

        // Try to load from specific providers.json if no base url
        if (string.IsNullOrEmpty(url))
        {
             var providersPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "providers.json");
             // Also check standard config path just in case
             if (!File.Exists(providersPath))
             {
                 providersPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "providers.json");
             }

             if (File.Exists(providersPath))
             {
                 try
                 {
                     var json = File.ReadAllText(providersPath);
                     var knownProviders = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                     if (knownProviders != null && knownProviders.TryGetValue(config.ProviderId, out var knownUrl))
                     {
                         url = knownUrl;
                     }
                 }
                 catch { /* Ignore config read error */ }
             }
        }

        if (!string.IsNullOrEmpty(url))
        {
            // url is set
        }
        else if (config.ProviderId.Contains("opencode", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://api.opencode.ai/v1/credits";
        }



        else if (config.ProviderId.Equals("xiaomi", StringComparison.OrdinalIgnoreCase))
        {
            // Hypothetical endpoint based on best effort
            url = "https://api.xiaomimimo.com/v1/user/balance";
        }
        else if (config.ProviderId.Equals("kilocode", StringComparison.OrdinalIgnoreCase) || 
                 config.ProviderId.Equals("kilo", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://api.kilocode.ai/v1/credits";
        }
        else if (config.ProviderId.Equals("synthetic", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://api.synthetic.new/v2/quotas";
        }

        else
        {
            return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("-", " ")),
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsageUnit = "Credits",
                IsQuotaBased = false,
                IsAvailable = false, // Hide from default view if untracked
                Description = "Configuration Required (Add 'base_url' to auth.json)",
                PlanType = PlanType.Usage
            }};
        }

        if (!url.StartsWith("http")) url = "https://" + url;
        
        // Smarter suffixing
        if (!url.EndsWith("/credits", StringComparison.OrdinalIgnoreCase) && 
            !url.Contains("/quota", StringComparison.OrdinalIgnoreCase) && 
            !url.Contains("billing", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("usage", StringComparison.OrdinalIgnoreCase) && 
            !url.Contains("balance", StringComparison.OrdinalIgnoreCase))
        {
             if (url.EndsWith("/v1"))
             {
                 url = url + "/credits";
             }
             else
             {
                 url = url.TrimEnd('/') + "/v1/credits";
             }
        }
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("-", " ").Replace(".", " ")),
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsageUnit = "Credits",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                IsAvailable = false,
                Description = $"API Error: {response.StatusCode}"
            }};
        }

        var responseString = await response.Content.ReadAsStringAsync();
        
        if (responseString.Trim().Equals("Not Found", StringComparison.OrdinalIgnoreCase))
        {
             return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = config.ProviderId,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsageUnit = "Credits",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                IsAvailable = true,
                Description = "Not Found (Invalid Key/URL)"
            }};
        }

        double total = 0;
        double used = 0;
        PlanType paymentType = PlanType.Usage;


        try
        {
            // Try standard OpenCode format
            var data = JsonSerializer.Deserialize<CreditsResponse>(responseString);
            if (data?.Data != null)
            {
                total = data.Data.TotalCredits;
                used = data.Data.UsedCredits;
                paymentType = PlanType.Usage;
            }
            else
            {
                // Try Synthetic format
                var synthetic = JsonSerializer.Deserialize<SyntheticResponse>(responseString);
                if (synthetic?.Subscription != null)
                {
                    total = synthetic.Subscription.Limit;
                    used = synthetic.Subscription.Requests;
                    paymentType = PlanType.Coding;
                }
                else
                {
                    // Try Kimi format
                    var kimi = JsonSerializer.Deserialize<KimiResponse>(responseString);
                    if (kimi?.Data != null)
                    {
                        total = kimi.Data.AvailableBalance;
                        used = 0; 
                        paymentType = PlanType.Usage;
                    }
                    else
                    {
                         // Try Minimax format
                        var minimax = JsonSerializer.Deserialize<MinimaxResponse>(responseString);
                        if (minimax?.Usage != null)
                        {
                            used = minimax.Usage.TokensUsed;
                            total = minimax.Usage.TokensLimit > 0 ? minimax.Usage.TokensLimit : 0; 
                            paymentType = PlanType.Usage;
                        }
                        else 
                        {
                            // Try Xiaomi format (assuming 'balance' or 'quota')
                            var xiaomi = JsonSerializer.Deserialize<XiaomiResponse>(responseString);
                            if (xiaomi?.Data != null)
                            {
                                total = xiaomi.Data.Balance;
                                used = 0;
                                paymentType = PlanType.Usage;
                            }
                            else
                            {
                                // Try Kilo Code format
                                var kilo = JsonSerializer.Deserialize<KiloResponse>(responseString);
                                if (kilo?.Data != null)
                                {
                                    total = kilo.Data.TotalCredits;
                                    used = kilo.Data.UsedCredits;
                                    paymentType = PlanType.Usage;
                                }
                                else
                                {
                                    throw new Exception("Unknown response format");
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
             // Try Synthetic format as fallback in catch block if first failed hard
            try 
             {
                var synthetic = JsonSerializer.Deserialize<SyntheticResponse>(responseString);
                if (synthetic?.Subscription != null)
                {
                    total = synthetic.Subscription.Limit;
                    used = synthetic.Subscription.Requests;
                    paymentType = PlanType.Coding;
                }
                else
                {
                     // Try Kimi fallback
                     var kimi = JsonSerializer.Deserialize<KimiResponse>(responseString);
                     if (kimi?.Data != null)
                     {
                        total = kimi.Data.AvailableBalance;
                        used = 0;
                        paymentType = PlanType.Usage;
                     }
                     else
                     {
                        throw new Exception("Failed to parse JSON as Credits, Synthetic, or Kimi response.");
                     }
                }
             }
             catch
             {
                throw new Exception($"Failed to parse JSON. Response start: '{responseString.Substring(0, Math.Min(responseString.Length, 50))}...'");
             }
        }

        var name = config.ProviderId;
        if (name == "generic-pay-as-you-go") name = new Uri(url).Host;

        if (total <= 0)
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.Replace("-", " ").Replace(".", " ")),
                    RequestsPercentage = 0,
                    RequestsUsed = used,
                    RequestsAvailable = total,
                    PlanType = paymentType,
                    UsageUnit = "Credits",
                    IsQuotaBased = paymentType == PlanType.Coding,
                    IsAvailable = false,
                    Description = "Usage unknown (missing quota totals)"
                }
            };
        }

        // ===================================================================
        // ⚠️  AI ASSISTANTS: DO NOT MODIFY THIS LOGIC WITHOUT DEVELOPER APPROVAL
        // ===================================================================
        // This calculation follows strict design rules documented in DESIGN.md
        // Quota providers MUST return REMAINING percentage (not used percentage)
        // Usage providers return USED percentage
        // See DESIGN.md section "Implementation Details" for full specification
        // When in doubt, ASK THE DEVELOPER FIRST before changing anything
        // ===================================================================
        
        // For quota-based providers, show remaining percentage (full bar = lots remaining, empty = depleted)
        // For other providers, show used percentage (full bar = high usage)
        var utilization = paymentType == PlanType.Coding
            ? ((total - used) / total) * 100.0
            : (used / total) * 100.0;

        string resetStr = "";
        DateTime? nextResetTime = null;
        try 
        {
            // Specifically handling Synthetic renewsAt if provided
            var synthetic = JsonSerializer.Deserialize<SyntheticResponse>(responseString);
            if (synthetic?.Subscription?.RenewsAt != null && DateTime.TryParse(synthetic.Subscription.RenewsAt, out var dt))
            {
                 var localDt = dt.ToLocalTime();
                 resetStr = $" (Resets: {localDt:MMM dd HH:mm})";
                 nextResetTime = localDt;
            }
        } catch { /* Suppress if not synthetic or parse fails */ }

        string usedStr = used == (int)used ? ((int)used).ToString(CultureInfo.InvariantCulture) : used.ToString("F2", CultureInfo.InvariantCulture);
        string totalStr = total == (int)total ? ((int)total).ToString(CultureInfo.InvariantCulture) : total.ToString("F2", CultureInfo.InvariantCulture);

        return new[] { new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.Replace("-", " ").Replace(".", " ")),
            RequestsPercentage = Math.Min(utilization, 100),
            RequestsUsed = used,
            RequestsAvailable = total,
            PlanType = paymentType,
            UsageUnit = "Credits",

            IsQuotaBased = false,
            Description = $"{usedStr} / {totalStr} credits{resetStr}",
            NextResetTime = nextResetTime
        }};
    }

    protected class CreditsResponse
    {
        [JsonPropertyName("data")]
        public CreditsData? Data { get; set; }
    }

    protected class CreditsData
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("used_credits")]
        public double UsedCredits { get; set; }
    }

    protected class SyntheticResponse
    {
        [JsonPropertyName("subscription")]
        public SyntheticSubscription? Subscription { get; set; }
    }

    protected class SyntheticSubscription
    {
        [JsonPropertyName("limit")]
        public double Limit { get; set; }

        [JsonPropertyName("requests")]
        public double Requests { get; set; }

        [JsonPropertyName("renewsAt")]
        public string? RenewsAt { get; set; }
    }

    protected class KimiResponse
    {
        [JsonPropertyName("data")]
        public KimiData? Data { get; set; }
    }

    protected class KimiData
    {
        [JsonPropertyName("available_balance")]
        public double AvailableBalance { get; set; }

        [JsonPropertyName("voucher_balance")]
        public double VoucherBalance { get; set; }
        
        [JsonPropertyName("cash_balance")]
        public double CashBalance { get; set; }
    }

    protected class MinimaxResponse
    {
        [JsonPropertyName("usage")]
        public MinimaxUsage? Usage { get; set; }
        
        [JsonPropertyName("base_resp")]
        public MinimaxBaseResp? BaseResp { get; set; }
    }
    
    protected class MinimaxUsage
    {
        [JsonPropertyName("tokens_used")]
        public double TokensUsed { get; set; }
        
        [JsonPropertyName("tokens_limit")]
        public double TokensLimit { get; set; } // Speculative
    }
    
    protected class MinimaxBaseResp
    {
        [JsonPropertyName("status_code")]
        public int StatusCode { get; set; }
    }

    private class XiaomiResponse
    {
         [JsonPropertyName("data")]
         public XiaomiData? Data { get; set; }
         
         [JsonPropertyName("code")]
         public int Code { get; set; }
    }
    
    private class XiaomiData
    {
        [JsonPropertyName("balance")]
        public double Balance { get; set; }
        
        [JsonPropertyName("quota")]
        public double Quota { get; set; }
    }

    private class KiloResponse
    {
        [JsonPropertyName("data")]
        public KiloData? Data { get; set; }
    }

    private class KiloData
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("used_credits")]
        public double UsedCredits { get; set; }
    }

    // Basic map for generic/Anthropic style where 'cost' or 'amount' is top level or in data
    // For now we just return 0 if unknown format to enable "Connected" status at least if 200 OK.
}
