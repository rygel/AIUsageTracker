// <copyright file="ClaudeCodeProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class ClaudeCodeProvider : ProviderBase
{
    /// <summary>
    /// The OAuth usage endpoint for Claude subscriptions.
    /// </summary>
    internal const string OAuthUsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    /// <summary>
    /// The beta header required for OAuth usage endpoint.
    /// </summary>
    internal const string OAuthBetaHeader = "oauth-2025-04-20";

    private readonly ILogger<ClaudeCodeProvider> _logger;
    private readonly HttpClient _httpClient;

    public ClaudeCodeProvider(ILogger<ClaudeCodeProvider> logger, HttpClient httpClient)
    {
        this._logger = logger;
        this._httpClient = httpClient;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "claude-code",
        displayName: "Claude Code",
        planType: PlanType.Usage,
        isQuotaBased: true,
        defaultConfigType: "quota-based",
        autoIncludeWhenUnconfigured: true,
        discoveryEnvironmentVariables: new[] { "ANTHROPIC_API_KEY", "CLAUDE_API_KEY" },
        iconAssetName: "anthropic",
        fallbackBadgeColorHex: "#FFA500",
        fallbackBadgeInitial: "C",
        authIdentityCandidatePathTemplates: new[]
        {
            "%USERPROFILE%\\.claude\\.credentials.json",
        },
        sessionAuthFileSchemas: new[]
        {
            new ProviderAuthFileSchema("claudeAiOauth", "accessToken"),
        });

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // Check if API key is configured
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = "Claude Code",
                IsAvailable = false,
                Description = "No API key configured",
                UsageUnit = "Status",
                IsQuotaBased = true,
                PlanType = PlanType.Usage,
                RawJson = "{\"source\":\"claude-code\",\"status\":\"api_key_missing\"}",
                HttpStatus = 401,
            },
            };
        }

        // Try OAuth usage endpoint first (for subscription users)
        try
        {
            var oauthUsage = await this.GetUsageFromOAuthAsync(config.ApiKey).ConfigureAwait(false);
            if (oauthUsage != null)
            {
                return new[] { oauthUsage };
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "OAuth usage endpoint not available, trying rate limit headers");
        }

        // Try to get usage from Anthropic API rate limit headers
        try
        {
            var apiUsage = await this.GetUsageFromApiAsync(config.ApiKey).ConfigureAwait(false);
            if (apiUsage != null)
            {
                return new[] { apiUsage };
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to get Claude usage from API, falling back to CLI");
        }

        // Fall back to CLI if API fails
        return await this.GetUsageFromCliAsync(config).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets usage information from the OAuth usage endpoint for subscription users.
    /// </summary>
    /// <param name="accessToken">The OAuth access token from credentials file.</param>
    /// <returns>Provider usage if successful, null otherwise.</returns>
    internal async Task<ProviderUsage?> GetUsageFromOAuthAsync(string accessToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, OAuthUsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("anthropic-beta", OAuthBetaHeader);

            using var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogDebug("OAuth usage endpoint returned {StatusCode}: {Body}", response.StatusCode, responseBody);
                return null;
            }

            var usageResponse = JsonSerializer.Deserialize<OAuthUsageResponse>(responseBody);
            if (usageResponse == null)
            {
                this._logger.LogWarning("Failed to deserialize OAuth usage response");
                return null;
            }

            return this.ParseOAuthUsageResponse(usageResponse, responseBody, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogDebug(ex, "OAuth usage endpoint request failed");
            return null;
        }
        catch (JsonException ex)
        {
            this._logger.LogWarning(ex, "Failed to parse OAuth usage response");
            return null;
        }
    }

    private ProviderUsage ParseOAuthUsageResponse(OAuthUsageResponse response, string rawJson, int httpStatus)
    {
        // Use 5-hour quota as primary (burst limit) and 7-day as secondary (rolling window)
        var primaryPercent = response.FiveHour?.Utilization ?? 0;
        var secondaryPercent = response.SevenDay?.Utilization ?? 0;

        // Determine the "main" percentage to show - use the higher of the two quotas
        var mainPercent = Math.Max(primaryPercent, secondaryPercent);

        // Build details for the sub-provider cards
        var details = new List<ProviderUsageDetail>();

        // 5-hour quota bucket
        if (response.FiveHour != null)
        {
            var fiveHourDetail = new ProviderUsageDetail
            {
                Name = "5-Hour Limit",
                DetailType = ProviderUsageDetailType.QuotaWindow,
                QuotaBucketKind = WindowKind.Primary,
                NextResetTime = response.FiveHour.ResetsAt,
            };
            fiveHourDetail.SetPercentageValue(
                response.FiveHour.Utilization,
                PercentageValueSemantic.Used,
                decimalPlaces: 0);
            details.Add(fiveHourDetail);
        }

        // 7-day quota bucket
        if (response.SevenDay != null)
        {
            var sevenDayDetail = new ProviderUsageDetail
            {
                Name = "7-Day Limit",
                DetailType = ProviderUsageDetailType.QuotaWindow,
                QuotaBucketKind = WindowKind.Secondary,
                NextResetTime = response.SevenDay.ResetsAt,
            };
            sevenDayDetail.SetPercentageValue(
                response.SevenDay.Utilization,
                PercentageValueSemantic.Used,
                decimalPlaces: 0);
            details.Add(sevenDayDetail);
        }

        // Model-specific breakdowns
        if (response.SevenDaySonnet != null)
        {
            var sonnetDetail = new ProviderUsageDetail
            {
                Name = "Sonnet (7-day)",
                DetailType = ProviderUsageDetailType.Model,
                QuotaBucketKind = WindowKind.None,
            };
            sonnetDetail.SetPercentageValue(
                response.SevenDaySonnet.Utilization,
                PercentageValueSemantic.Used,
                decimalPlaces: 0);
            details.Add(sonnetDetail);
        }

        if (response.SevenDayOpus != null)
        {
            var opusDetail = new ProviderUsageDetail
            {
                Name = "Opus (7-day)",
                DetailType = ProviderUsageDetailType.Model,
                QuotaBucketKind = WindowKind.None,
            };
            opusDetail.SetPercentageValue(
                response.SevenDayOpus.Utilization,
                PercentageValueSemantic.Used,
                decimalPlaces: 0);
            details.Add(opusDetail);
        }

        // Determine reset time - use the sooner of the two quota resets
        DateTime? nextReset = null;
        if (response.FiveHour?.ResetsAt != null && response.SevenDay?.ResetsAt != null)
        {
            nextReset = response.FiveHour.ResetsAt < response.SevenDay.ResetsAt
                ? response.FiveHour.ResetsAt
                : response.SevenDay.ResetsAt;
        }
        else
        {
            nextReset = response.FiveHour?.ResetsAt ?? response.SevenDay?.ResetsAt;
        }

        // Build description
        var description = $"5h: {primaryPercent:F0}% | 7d: {secondaryPercent:F0}%";
        if (response.ExtraUsage?.IsEnabled == true)
        {
            description += " | Extra usage enabled";
        }

        // For quota-based providers, RequestsPercentage represents REMAINING percentage
        // The UI expects this semantic: higher RequestsPercentage = more quota remaining
        var remainingPercent = 100 - mainPercent;

        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = "Claude Code",
            RequestsPercentage = remainingPercent,
            RequestsUsed = mainPercent,
            RequestsAvailable = 100,
            UsageUnit = "%",
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            IsAvailable = true,
            Description = description,
            Details = details,
            NextResetTime = nextReset,
            RawJson = rawJson,
            HttpStatus = httpStatus,
        };
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

            using var testResponse = await this._httpClient.SendAsync(testRequest).ConfigureAwait(false);
            var responseBody = await testResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Extract rate limit information from headers
            var rateLimitHeaders = this.ExtractRateLimitInfo(testResponse.Headers);

            // Log response for debugging
            this._logger.LogDebug($"Claude API test call: Status={testResponse.StatusCode}, RPM={rateLimitHeaders.RequestsRemaining}/{rateLimitHeaders.RequestsLimit}");

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
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Rate Limit Tier", Used = rateLimitHeaders.GetTierName(), DetailType = ProviderUsageDetailType.Other, QuotaBucketKind = WindowKind.None });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Requests/min Limit", Used = rateLimitHeaders.RequestsLimit.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), DetailType = ProviderUsageDetailType.Other, QuotaBucketKind = WindowKind.None });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Requests/min Remaining", Used = rateLimitHeaders.RequestsRemaining.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), DetailType = ProviderUsageDetailType.Other, QuotaBucketKind = WindowKind.None });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Input Tokens/min Limit", Used = rateLimitHeaders.InputTokensLimit.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), DetailType = ProviderUsageDetailType.Other, QuotaBucketKind = WindowKind.None });
                tooltipDetails.Add(new ProviderUsageDetail { Name = "Input Tokens/min Remaining", Used = rateLimitHeaders.InputTokensRemaining.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), DetailType = ProviderUsageDetailType.Other, QuotaBucketKind = WindowKind.None });
                tooltipDetails.Add(new ProviderUsageDetail
                {
                    Name = "Current RPM Usage",
                    DetailType = ProviderUsageDetailType.Other,
                    QuotaBucketKind = WindowKind.None,
                    PercentageValue = usagePercentage,
                    PercentageSemantic = PercentageValueSemantic.Used,
                    PercentageDecimalPlaces = 1,
                });

                return new ProviderUsage
                {
                    ProviderId = this.ProviderId,
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
                    AccountName = warningMessage ?? string.Empty, // Using AccountName to carry warning state
                    RawJson = responseBody,
                    HttpStatus = (int)testResponse.StatusCode,
                };
            }

            // No rate limit headers found
            return null;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error calling Anthropic API");
            return null;
        }
    }

    private RateLimitInfo ExtractRateLimitInfo(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        var info = new RateLimitInfo();

        if (headers.TryGetValues("anthropic-ratelimit-requests-limit", out var requestLimitValues))
        {
            if (int.TryParse(requestLimitValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var limit))
            {
                info.RequestsLimit = limit;
            }
        }

        if (headers.TryGetValues("anthropic-ratelimit-requests-remaining", out var requestRemainingValues))
        {
            if (int.TryParse(requestRemainingValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var remaining))
            {
                info.RequestsRemaining = remaining;
            }
        }

        if (headers.TryGetValues("anthropic-ratelimit-input-tokens-limit", out var inputLimitValues))
        {
            if (int.TryParse(inputLimitValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var inputLimit))
            {
                info.InputTokensLimit = inputLimit;
            }
        }

        if (headers.TryGetValues("anthropic-ratelimit-input-tokens-remaining", out var inputRemainingValues))
        {
            if (int.TryParse(inputRemainingValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var inputRemaining))
            {
                info.InputTokensRemaining = inputRemaining;
            }
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
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    // CLI not found, but key is configured - show as available
                    return new[]
                    {
                        new ProviderUsage
                    {
                        ProviderId = this.ProviderId,
                        ProviderName = "Claude Code",
                        IsAvailable = true,
                        Description = "Connected (API key configured)",
                        UsageUnit = "Status",
                        IsQuotaBased = false,
                        PlanType = PlanType.Usage,
                        RawJson = "{\"source\":\"claude-cli\",\"status\":\"process_start_failed\"}",
                        HttpStatus = 503,
                    },
                    };
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    this._logger.LogWarning("Claude Code CLI timed out");
                }

                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    this._logger.LogWarning($"Claude Code CLI failed: {error}");

                    // CLI failed, but key is configured - show as available
                    return new[]
                    {
                        new ProviderUsage
                    {
                        ProviderId = this.ProviderId,
                        ProviderName = "Claude Code",
                        IsAvailable = true,
                        Description = "Connected (API key configured)",
                        UsageUnit = "Status",
                        IsQuotaBased = false,
                        PlanType = PlanType.Usage,
                        RawJson = string.IsNullOrWhiteSpace(error) ? "{\"source\":\"claude-cli\",\"status\":\"failed\"}" : error,
                        HttpStatus = 500,
                    },
                    };
                }

                return new[] { this.ParseCliOutput(output) };
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to run Claude Code CLI");

                // Exception occurred, but key is configured - show as available
                return new[]
                {
                    new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = "Claude Code",
                    IsAvailable = true,
                    Description = "Connected (API key configured)",
                    UsageUnit = "Status",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    RawJson = ex.ToString(),
                    HttpStatus = 500,
                },
                };
            }
        }).ConfigureAwait(false);
    }

    private ProviderUsage ParseCliOutput(string output)
    {
        // Parse Claude Code usage output
        double currentUsage = 0;
        double budgetLimit = 0;

        var usageMatch = Regex.Match(output, @"Current Usage[:\s]+\$?(?<usage>[0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        if (usageMatch.Success)
        {
            double.TryParse(usageMatch.Groups["usage"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out currentUsage);
        }

        var budgetMatch = Regex.Match(output, @"Budget Limit[:\s]+\$?(?<budget>[0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        if (budgetMatch.Success)
        {
            double.TryParse(budgetMatch.Groups["budget"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out budgetLimit);
        }

        var remainingMatch = Regex.Match(output, @"Remaining[:\s]+\$?(?<remaining>[0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
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
            ProviderId = this.ProviderId,
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
                : $"${currentUsage.ToString("F2", CultureInfo.InvariantCulture)} used",
            RawJson = output,
            HttpStatus = 200,
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
            return this.RequestsLimit switch
            {
                <= 50 => "Tier 1",
                <= 1000 => "Tier 2",
                <= 2000 => "Tier 3",
                <= 4000 => "Tier 4",
                _ => "Custom",
            };
        }
    }

    /// <summary>
    /// Response model for the OAuth usage endpoint.
    /// </summary>
    internal class OAuthUsageResponse
    {
        [JsonPropertyName("five_hour")]
        public OAuthQuotaBucket? FiveHour { get; set; }

        [JsonPropertyName("seven_day")]
        public OAuthQuotaBucket? SevenDay { get; set; }

        [JsonPropertyName("seven_day_sonnet")]
        public OAuthModelQuota? SevenDaySonnet { get; set; }

        [JsonPropertyName("seven_day_opus")]
        public OAuthModelQuota? SevenDayOpus { get; set; }

        [JsonPropertyName("extra_usage")]
        public OAuthExtraUsage? ExtraUsage { get; set; }
    }

    /// <summary>
    /// Quota bucket with utilization percentage and reset time.
    /// </summary>
    internal class OAuthQuotaBucket
    {
        [JsonPropertyName("utilization")]
        public double Utilization { get; set; }

        [JsonPropertyName("resets_at")]
        public DateTime? ResetsAt { get; set; }
    }

    /// <summary>
    /// Model-specific quota information.
    /// </summary>
    internal class OAuthModelQuota
    {
        [JsonPropertyName("utilization")]
        public double Utilization { get; set; }
    }

    /// <summary>
    /// Extra usage (overage) information.
    /// </summary>
    internal class OAuthExtraUsage
    {
        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }
    }
}
