using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class OpenAIProvider : IProviderService
{
    public string ProviderId => "openai";
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProvider> _logger;

    public OpenAIProvider(HttpClient httpClient, ILogger<OpenAIProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ArgumentException("API Key or Access Token is missing via environment variable or auth.json");
        }

        if (config.ApiKey.StartsWith("sk-proj"))
        {
             return new[] { new ProviderUsage
             {
                 ProviderId = ProviderId,
                 ProviderName = "OpenAI",
                 IsAvailable = false,
                 Description = "Project keys (sk-proj-...) not supported. Use a standard user API key or JWT token."
             }};
        }

        bool isJwtToken = config.ApiKey.StartsWith("eyJ") && config.ApiKey.Split('.').Length >= 3;

        if (isJwtToken)
        {
            return await GetUsageFromChatGPTBackend(config);
        }
        else
        {
            return await GetUsageFromAPIKey(config);
        }
    }

    private async Task<IEnumerable<ProviderUsage>> GetUsageFromChatGPTBackend(ProviderConfig config)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            
            var response = await _httpClient.SendAsync(request);
            var jsonContent = await response.Content.ReadAsStringAsync();
            var usageData = JsonSerializer.Deserialize<JsonElement>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (response.IsSuccessStatusCode && usageData.ValueKind == JsonValueKind.Object)
            {
                string planType = "Unknown";
                if (usageData.TryGetProperty("plan_type", out var planProp))
                {
                    planType = planProp.GetString() ?? "Unknown";
                }

                double primaryUsed = 0;
                if (usageData.TryGetProperty("rate_limit", out var rateLimit) 
                    && rateLimit.TryGetProperty("primary_window", out var primaryWindow))
                {
                    if (primaryWindow.TryGetProperty("used_percent", out var primaryUsedProp))
                    {
                        primaryUsed = primaryUsedProp.GetDouble();
                    }
                }

                double secondaryUsed = 0;
                if (usageData.TryGetProperty("rate_limit", out var rateLimit2) 
                    && rateLimit2.TryGetProperty("secondary_window", out var secondaryWindow))
                {
                    if (secondaryWindow.TryGetProperty("used_percent", out var secondaryUsedProp))
                    {
                        secondaryUsed = secondaryUsedProp.GetDouble();
                    }
                }
                
                double? creditsBalance = null;
                bool isUnlimited = false;
                if (usageData.TryGetProperty("credits", out var credits))
                {
                    if (credits.TryGetProperty("balance", out var balanceProp))
                    {
                        creditsBalance = balanceProp.GetDouble();
                    }
                    if (credits.TryGetProperty("unlimited", out var unlimitedProp) 
                        && unlimitedProp.ValueKind == JsonValueKind.True)
                    {
                        isUnlimited = true;
                    }
                }

                var description = planType;
                if (creditsBalance.HasValue)
                {
                    description += $" | Balance: ${creditsBalance:F2}";
                    if (isUnlimited)
                    {
                        description += " (Unlimited)";
                    }
                }

                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = true,
                    UsagePercentage = primaryUsed / 100,
                    PaymentType = creditsBalance.HasValue ? PaymentType.Credits : PaymentType.UsageBased,
                    Description = description,
                    CostUsed = creditsBalance ?? 0,
                    UsageUnit = isUnlimited ? "Unlimited" : "Tokens"
                }};
            }
            else
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = false,
                    Description = $"ChatGPT API Error ({response.StatusCode})"
                }};
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI ChatGPT backend check failed");
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "OpenAI",
                IsAvailable = false,
                Description = "ChatGPT Connection Failed"
            }};
        }
    }

    private async Task<IEnumerable<ProviderUsage>> GetUsageFromAPIKey(ProviderConfig config)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
             var response = await _httpClient.SendAsync(request);
             
              if (response.IsSuccessStatusCode)
              {
                  return new[] { new ProviderUsage
                  {
                     ProviderId = ProviderId,
                     ProviderName = "OpenAI",
                     IsAvailable = true,
                     UsagePercentage = 0,
                     IsQuotaBased = false,
                     PaymentType = PaymentType.UsageBased,
                     Description = "Connected (API Key - Check Dashboard)",

                     UsageUnit = "Status"
                  }};
              }
              else
              {
                  return new[] { new ProviderUsage
                  {
                     ProviderId = ProviderId,
                     ProviderName = "OpenAI",
                     IsAvailable = false,
                     Description = $"Invalid API Key ({response.StatusCode})"
                  }};
              }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI API check failed");
             return new[] { new ProviderUsage
             {
                 ProviderId = ProviderId,
                 ProviderName = "OpenAI",
                 IsAvailable = false,
                 Description = "Connection Failed"
             }};
        }
    }
}
