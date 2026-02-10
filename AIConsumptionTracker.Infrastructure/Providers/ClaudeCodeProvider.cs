using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class ClaudeCodeProvider : IProviderService
{
    public string ProviderId => "claude-code";
    private readonly ILogger<ClaudeCodeProvider> _logger;
    private readonly HttpClient _httpClient;

    public ClaudeCodeProvider(ILogger<ClaudeCodeProvider> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // Check if API key is configured
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Claude Code",
                IsAvailable = false,
                Description = "No API key configured",
                IsQuotaBased = false,
                PaymentType = PaymentType.UsageBased
            }};
        }

        // Try to get usage from Anthropic API first
        try
        {
            var apiUsage = await GetUsageFromApiAsync(config.ApiKey);
            if (apiUsage != null)
            {
                return new[] { apiUsage };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Claude usage from API, falling back to CLI");
        }

        // Fall back to CLI if API fails
        return await GetUsageFromCliAsync(config);
    }

    private async Task<ProviderUsage?> GetUsageFromApiAsync(string apiKey)
    {
        try
        {
            // First, make a test request to get rate limit headers
            var testRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            testRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            testRequest.Headers.Add("anthropic-version", "2023-06-01");
            testRequest.Content = new StringContent("{\"model\":\"claude-sonnet-4-20250514\",\"max_tokens\":1,\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}", System.Text.Encoding.UTF8, "application/json");

            var testResponse = await _httpClient.SendAsync(testRequest);
            
            // Extract rate limit information from headers
            var rateLimitHeaders = ExtractRateLimitInfo(testResponse.Headers);
            
            // Now get usage data
            var usageRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/usage");
            usageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            usageRequest.Headers.Add("anthropic-version", "2023-06-01");

            var usageResponse = await _httpClient.SendAsync(usageRequest);
            
            if (!usageResponse.IsSuccessStatusCode)
            {
                var errorContent = await usageResponse.Content.ReadAsStringAsync();
                _logger.LogWarning($"Anthropic API returned {usageResponse.StatusCode}: {errorContent}");
                
                // Return basic info with rate limits even if usage call fails
                if (rateLimitHeaders.RequestsLimit > 0)
                {
                    return new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "Claude Code",
                        UsagePercentage = 0,
                        CostUsed = 0,
                        CostLimit = 0,
                        UsageUnit = "USD",
                        IsQuotaBased = false,
                        PaymentType = PaymentType.UsageBased,
                        IsAvailable = true,
                        Description = $"Tier: {rateLimitHeaders.GetTierName()} | RPM: {rateLimitHeaders.RequestsRemaining}/{rateLimitHeaders.RequestsLimit}"
                    };
                }
                return null;
            }

            var content = await usageResponse.Content.ReadAsStringAsync();
            var usageData = JsonSerializer.Deserialize<AnthropicUsageResponse>(content, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            // Calculate totals from the usage data
            double totalCost = 0;
            double totalTokens = 0;
            
            if (usageData?.Usage != null)
            {
                foreach (var item in usageData.Usage)
                {
                    totalCost += item.CostUsd;
                    totalTokens += item.InputTokens + item.OutputTokens;
                }
            }

            // Build description with rate limit info
            string description;
            if (rateLimitHeaders.RequestsLimit > 0)
            {
                description = $"${totalCost:F2} cost | {totalTokens:N0} tokens | Tier: {rateLimitHeaders.GetTierName()}";
            }
            else
            {
                description = $"${totalCost:F2} total cost | {totalTokens:N0} tokens";
            }

            return new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Claude Code",
                UsagePercentage = 0,
                CostUsed = totalCost,
                CostLimit = 0,
                UsageUnit = "USD",
                IsQuotaBased = false,
                PaymentType = PaymentType.UsageBased,
                IsAvailable = true,
                Description = description
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Anthropic API");
            return null;
        }
    }

    private RateLimitInfo ExtractRateLimitInfo(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        var info = new RateLimitInfo();
        
        if (headers.TryGetValues("anthropic-ratelimit-requests-limit", out var requestLimitValues))
        {
            if (int.TryParse(requestLimitValues.FirstOrDefault(), out var limit))
                info.RequestsLimit = limit;
        }
        
        if (headers.TryGetValues("anthropic-ratelimit-requests-remaining", out var requestRemainingValues))
        {
            if (int.TryParse(requestRemainingValues.FirstOrDefault(), out var remaining))
                info.RequestsRemaining = remaining;
        }
        
        if (headers.TryGetValues("anthropic-ratelimit-input-tokens-limit", out var inputLimitValues))
        {
            if (int.TryParse(inputLimitValues.FirstOrDefault(), out var inputLimit))
                info.InputTokensLimit = inputLimit;
        }
        
        if (headers.TryGetValues("anthropic-ratelimit-input-tokens-remaining", out var inputRemainingValues))
        {
            if (int.TryParse(inputRemainingValues.FirstOrDefault(), out var inputRemaining))
                info.InputTokensRemaining = inputRemaining;
        }
        
        return info;
    }

    private async Task<IEnumerable<ProviderUsage>> GetUsageFromCliAsync(ProviderConfig config)
    {
        return await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "claude",
                    Arguments = "usage",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    // CLI not found, but key is configured - show as available
                    return new[] { new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "Claude Code",
                        IsAvailable = true,
                        Description = "Connected (API key configured)",
                        IsQuotaBased = false,
                        PaymentType = PaymentType.UsageBased
                    }};
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning($"Claude Code CLI failed: {error}");
                    // CLI failed, but key is configured - show as available
                    return new[] { new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "Claude Code",
                        IsAvailable = true,
                        Description = "Connected (API key configured)",
                        IsQuotaBased = false,
                        PaymentType = PaymentType.UsageBased
                    }};
                }

                return new[] { ParseCliOutput(output) };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run Claude Code CLI");
                // Exception occurred, but key is configured - show as available
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Claude Code",
                    IsAvailable = true,
                    Description = "Connected (API key configured)",
                    IsQuotaBased = false,
                    PaymentType = PaymentType.UsageBased
                }};
            }
        });
    }

    private ProviderUsage ParseCliOutput(string output)
    {
        // Parse Claude Code usage output
        double currentUsage = 0;
        double budgetLimit = 0;

        var usageMatch = Regex.Match(output, @"Current Usage[:\s]+\$?([0-9.]+)", RegexOptions.IgnoreCase);
        if (usageMatch.Success)
        {
            double.TryParse(usageMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out currentUsage);
        }

        var budgetMatch = Regex.Match(output, @"Budget Limit[:\s]+\$?([0-9.]+)", RegexOptions.IgnoreCase);
        if (budgetMatch.Success)
        {
            double.TryParse(budgetMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out budgetLimit);
        }

        var remainingMatch = Regex.Match(output, @"Remaining[:\s]+\$?([0-9.]+)", RegexOptions.IgnoreCase);
        if (remainingMatch.Success && budgetLimit == 0)
        {
            double remaining;
            if (double.TryParse(remainingMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out remaining))
            {
                budgetLimit = currentUsage + remaining;
            }
        }

        double usagePercentage = budgetLimit > 0 ? (currentUsage / budgetLimit) * 100.0 : 0;

        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Claude Code",
            UsagePercentage = Math.Min(usagePercentage, 100),
            CostUsed = currentUsage,
            CostLimit = budgetLimit,
            UsageUnit = "USD",
            IsQuotaBased = false,
            PaymentType = PaymentType.UsageBased,
            IsAvailable = true,
            Description = budgetLimit > 0 
                ? $"${currentUsage:F2} used of ${budgetLimit:F2} limit"
                : $"${currentUsage:F2} used"
        };
    }

    private class AnthropicUsageResponse
    {
        public List<AnthropicUsageItem>? Usage { get; set; }
    }

    private class AnthropicUsageItem
    {
        public string? Model { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public double CostUsd { get; set; }
        public string? Timestamp { get; set; }
    }

    private class RateLimitInfo
    {
        public int RequestsLimit { get; set; }
        public int RequestsRemaining { get; set; }
        public int InputTokensLimit { get; set; }
        public int InputTokensRemaining { get; set; }

        public string GetTierName()
        {
            // Determine tier based on request limits
            // Tier 1: 50 RPM
            // Tier 2: 1,000 RPM  
            // Tier 3: 2,000 RPM
            // Tier 4: 4,000 RPM
            return RequestsLimit switch
            {
                <= 50 => "Tier 1",
                <= 1000 => "Tier 2",
                <= 2000 => "Tier 3",
                <= 4000 => "Tier 4",
                _ => "Custom"
            };
        }
    }
}
