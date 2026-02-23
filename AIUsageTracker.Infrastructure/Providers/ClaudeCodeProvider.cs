using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

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
                UsageUnit = "Status",
                IsQuotaBased = false,
                PlanType = PlanType.Usage
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
            // Make a test request to get rate limit headers
            // Note: Anthropic API doesn't have a usage endpoint, so we use rate limits from headers
            var testRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            testRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            testRequest.Headers.Add("anthropic-version", "2023-06-01");
            testRequest.Content = new StringContent("{\"model\":\"claude-sonnet-4-20250514\",\"max_tokens\":1,\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}", System.Text.Encoding.UTF8, "application/json");

            using var testResponse = await _httpClient.SendAsync(testRequest);
            
            // Extract rate limit information from headers
            var rateLimitHeaders = ExtractRateLimitInfo(testResponse.Headers);
            
            // Log response for debugging
            _logger.LogDebug($"Claude API test call: Status={testResponse.StatusCode}, RPM={rateLimitHeaders.RequestsRemaining}/{rateLimitHeaders.RequestsLimit}");
            
            // Even if the request fails (e.g., 429 rate limited), we can still get rate limit headers
            if (rateLimitHeaders.RequestsLimit > 0)
            {
                // Calculate usage percentage based on rate limits
                double usagePercentage = 0;
                string? warningMessage = null;
                
                // Calculate percentage: (limit - remaining) / limit * 100
                var used = rateLimitHeaders.RequestsLimit - rateLimitHeaders.RequestsRemaining;
                usagePercentage = (used / (double)rateLimitHeaders.RequestsLimit) * 100.0;
                
                // Determine warning level
                if (usagePercentage >= 90)
                {
                    warningMessage = "⚠️ CRITICAL: Approaching rate limit!";
                }
                else if (usagePercentage >= 70)
                {
                    warningMessage = "⚠️ WARNING: High usage";
                }

                // Build description with rate limit info
                var description = $"Tier: {rateLimitHeaders.GetTierName()} | Used: {used}/{rateLimitHeaders.RequestsLimit} RPM ({usagePercentage:F0}%)";

                // Build detailed tooltip info
                var tooltipDetails = new List<ProviderUsageDetail>();
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Rate Limit Tier", Used = rateLimitHeaders.GetTierName() });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Requests/min Limit", Used = rateLimitHeaders.RequestsLimit.ToString("N0") });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Requests/min Remaining", Used = rateLimitHeaders.RequestsRemaining.ToString("N0") });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Input Tokens/min Limit", Used = rateLimitHeaders.InputTokensLimit.ToString("N0") });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Input Tokens/min Remaining", Used = rateLimitHeaders.InputTokensRemaining.ToString("N0") });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Current RPM Usage", Used = $"{usagePercentage:F1}%" });

                return new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Claude Code",
                    RequestsPercentage = usagePercentage,
                    RequestsUsed = 0, // Anthropic doesn't provide cost via API
                    RequestsAvailable = 0,
                    UsageUnit = "RPM",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    IsAvailable = true,
                    Description = description,
                    Details = tooltipDetails,
                    AccountName = warningMessage // Using AccountName to carry warning state
                };
            }
            
            // No rate limit headers found
            return null;
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
        return await Task.Run(async () =>
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
                        UsageUnit = "Status",
                        IsQuotaBased = false,
                        PlanType = PlanType.Usage
                    }};
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit(5000);
                var output = await outputTask;
                var error = await errorTask;

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
                        UsageUnit = "Status",
                        IsQuotaBased = false,
                        PlanType = PlanType.Usage
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
                    UsageUnit = "Status",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
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
            RequestsPercentage = Math.Min(usagePercentage, 100),
            RequestsUsed = currentUsage,
            RequestsAvailable = budgetLimit,
            UsageUnit = "USD",
            IsQuotaBased = false,
            PlanType = PlanType.Usage,
            IsAvailable = true,
            Description = budgetLimit > 0 
                ? $"${currentUsage.ToString("F2", CultureInfo.InvariantCulture)} used of ${budgetLimit.ToString("F2", CultureInfo.InvariantCulture)} limit"
                : $"${currentUsage.ToString("F2", CultureInfo.InvariantCulture)} used"
        };
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

