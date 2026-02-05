using System.Text.Json;
using System.Text.Json.Serialization;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class GenericPayAsYouGoProvider : IProviderService
{
    public string ProviderId => "generic-pay-as-you-go";
    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericPayAsYouGoProvider> _logger;

    public GenericPayAsYouGoProvider(HttpClient httpClient, ILogger<GenericPayAsYouGoProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
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
        else if (config.ProviderId.Equals("minimax", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://api.minimax.chat/v1/user/usage";
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

        else
        {
            return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("-", " ")),
                UsagePercentage = 0,
                CostUsed = 0,
                CostLimit = 0,
                UsageUnit = "Credits",
                IsQuotaBased = false,
                IsAvailable = false, // Hide from default view if untracked
                Description = "Configuration Required (Add 'base_url' to auth.json)"
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
            throw new Exception($"API returned {response.StatusCode} for {url}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        
        if (responseString.Trim().Equals("Not Found", StringComparison.OrdinalIgnoreCase))
        {
             return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = config.ProviderId,
                UsagePercentage = 0,
                CostUsed = 0,
                CostLimit = 0,
                UsageUnit = "Credits",
                IsQuotaBased = false,
                IsAvailable = true,
                Description = "Not Found (Invalid Key/URL)"
            }};
        }

        double total = 0;
        double used = 0;
        PaymentType paymentType = PaymentType.UsageBased;


        try
        {
            // Try standard OpenCode format
            var data = JsonSerializer.Deserialize<CreditsResponse>(responseString);
            if (data?.Data != null)
            {
                total = data.Data.TotalCredits;
                used = data.Data.UsedCredits;
                paymentType = PaymentType.Credits;
            }
            else
            {
                // Try Synthetic format
                var synthetic = JsonSerializer.Deserialize<SyntheticResponse>(responseString);
                if (synthetic?.Subscription != null)
                {
                    total = synthetic.Subscription.Limit;
                    used = synthetic.Subscription.Requests;
                    paymentType = PaymentType.Quota;
                }
                else
                {
                    // Try Kimi format
                    var kimi = JsonSerializer.Deserialize<KimiResponse>(responseString);
                    if (kimi?.Data != null)
                    {
                        total = kimi.Data.AvailableBalance;
                        used = 0; 
                        paymentType = PaymentType.Credits;
                    }
                    else
                    {
                         // Try Minimax format
                        var minimax = JsonSerializer.Deserialize<MinimaxResponse>(responseString);
                        if (minimax?.Usage != null)
                        {
                            used = minimax.Usage.TokensUsed;
                            total = minimax.Usage.TokensLimit > 0 ? minimax.Usage.TokensLimit : 0; 
                            paymentType = PaymentType.UsageBased;
                        }
                        else 
                        {
                            // Try Xiaomi format (assuming 'balance' or 'quota')
                            var xiaomi = JsonSerializer.Deserialize<XiaomiResponse>(responseString);
                            if (xiaomi?.Data != null)
                            {
                                total = xiaomi.Data.Balance;
                                used = 0;
                                paymentType = PaymentType.Credits;
                            }
                            else
                            {
                                // Try Kilo Code format
                                var kilo = JsonSerializer.Deserialize<KiloResponse>(responseString);
                                if (kilo?.Data != null)
                                {
                                    total = kilo.Data.TotalCredits;
                                    used = kilo.Data.UsedCredits;
                                    paymentType = PaymentType.Credits;
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
                    paymentType = PaymentType.Quota;
                }
                else
                {
                     // Try Kimi fallback
                     var kimi = JsonSerializer.Deserialize<KimiResponse>(responseString);
                     if (kimi?.Data != null)
                     {
                        total = kimi.Data.AvailableBalance;
                        used = 0;
                        paymentType = PaymentType.Credits;
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

        var utilization = total > 0 ? (used / total) * 100.0 : 0;

        var name = config.ProviderId;
        if (name == "generic-pay-as-you-go") name = new Uri(url).Host;

        string resetStr = "";
        DateTime? nextResetTime = null;
        try 
        {
            // Specifically handling Synthetic renewsAt if provided
            var synthetic = JsonSerializer.Deserialize<SyntheticResponse>(responseString);
            if (synthetic?.Subscription?.RenewsAt != null && DateTime.TryParse(synthetic.Subscription.RenewsAt, out var dt))
            {
                 var diff = dt.ToLocalTime() - DateTime.Now;
                 if (diff.TotalSeconds > 0)
                 {
                     resetStr = $" (Resets: ({dt.ToLocalTime():MMM dd HH:mm}))";
                     nextResetTime = dt.ToLocalTime();
                 }
            }
        } catch { /* Suppress if not synthetic or parse fails */ }

        return new[] { new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.Replace("-", " ").Replace(".", " ")),
            UsagePercentage = Math.Min(utilization, 100),
            CostUsed = used,
            CostLimit = total,
            PaymentType = paymentType,
            UsageUnit = "Credits",

            IsQuotaBased = false,
            Description = $"{used:F2} / {total:F2} credits{resetStr}",
            NextResetTime = nextResetTime
        }};
    }

    private class CreditsResponse
    {
        [JsonPropertyName("data")]
        public CreditsData? Data { get; set; }
    }

    private class CreditsData
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("used_credits")]
        public double UsedCredits { get; set; }
    }

    private class SyntheticResponse
    {
        [JsonPropertyName("subscription")]
        public SyntheticSubscription? Subscription { get; set; }
    }

    private class SyntheticSubscription
    {
        [JsonPropertyName("limit")]
        public double Limit { get; set; }

        [JsonPropertyName("requests")]
        public double Requests { get; set; }

        [JsonPropertyName("renewsAt")]
        public string? RenewsAt { get; set; }
    }

    private class KimiResponse
    {
        [JsonPropertyName("data")]
        public KimiData? Data { get; set; }
    }

    private class KimiData
    {
        [JsonPropertyName("available_balance")]
        public double AvailableBalance { get; set; }

        [JsonPropertyName("voucher_balance")]
        public double VoucherBalance { get; set; }
        
        [JsonPropertyName("cash_balance")]
        public double CashBalance { get; set; }
    }

    private class MinimaxResponse
    {
        [JsonPropertyName("usage")]
        public MinimaxUsage? Usage { get; set; }
        
        [JsonPropertyName("base_resp")]
        public MinimaxBaseResp? BaseResp { get; set; }
    }
    
    private class MinimaxUsage
    {
        [JsonPropertyName("tokens_used")]
        public double TokensUsed { get; set; }
        
        [JsonPropertyName("tokens_limit")]
        public double TokensLimit { get; set; } // Speculative
    }
    
    private class MinimaxBaseResp
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

