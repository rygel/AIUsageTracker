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
        "claude-code",
        "Claude Code",
        PlanType.Usage,
        isQuotaBased: true)
    {
        DiscoveryEnvironmentVariables = new[] { "ANTHROPIC_API_KEY", "CLAUDE_API_KEY" },
        IconAssetName = "anthropic",
        BadgeColorHex = "#FFA500",
        BadgeInitial = "C",
        AuthIdentityCandidatePathTemplates = new[]
        {
            "%USERPROFILE%\\.claude\\.credentials.json",
        },
        SessionAuthFileSchemas = new[]
        {
            new ProviderAuthFileSchema("claudeAiOauth", "accessToken"),
        },
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Burst,         "5h",     ChildProviderId: "claude-code.current-session", SettingsLabel: "Current Session (5-hour quota)", DetailName: "Current Session", PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.ModelSpecific, "Sonnet", ChildProviderId: "claude-code.sonnet",         SettingsLabel: "Sonnet (7-day model quota)",    DetailName: "Sonnet",          PeriodDuration: TimeSpan.FromDays(7)),
            new(WindowKind.ModelSpecific, "Opus",   ChildProviderId: "claude-code.opus",           SettingsLabel: "Opus (7-day model quota)",      DetailName: "Opus",            PeriodDuration: TimeSpan.FromDays(7)),
            new(WindowKind.Rolling,       "7-day",  ChildProviderId: "claude-code.all-models",     SettingsLabel: "All Models (7-day combined)",   DetailName: "All Models",      PeriodDuration: TimeSpan.FromDays(7)),
        },
        FamilyMode = ProviderFamilyMode.FlatWindowCards,
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        // Check if API key is configured
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = this.Definition.DisplayName,
                IsAvailable = false,
                Description = "No API key configured",
                State = ProviderUsageState.Missing,
                IsStatusOnly = true,
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                RawJson = "{\"source\":\"claude-code\",\"status\":\"api_key_missing\"}",
                HttpStatus = 401,
            },
            };
        }

        // Re-read the credentials file to get the freshest OAuth token.
        // The Claude Code CLI refreshes the token periodically and writes it back
        // to .credentials.json. Using the stale config.ApiKey would fail once the
        // token expires (typically within 1 hour).
        var effectiveApiKey = config.ApiKey;
        var isOAuthToken = effectiveApiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);
        if (isOAuthToken)
        {
            var freshToken = this.ReadFreshOAuthToken();
            if (!string.IsNullOrEmpty(freshToken))
            {
                effectiveApiKey = freshToken;
            }
        }

        // Try OAuth usage endpoint first (for subscription users)
        try
        {
            var oauthUsages = await this.GetUsageFromOAuthAsync(effectiveApiKey).ConfigureAwait(false);
            if (oauthUsages != null)
            {
                return oauthUsages;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "OAuth usage endpoint not available, trying rate limit headers");
        }

        // Skip the API rate-limit probe when the token is an OAuth token — it will
        // always return 401 because OAuth tokens are not API keys.
        if (!isOAuthToken)
        {
            try
            {
                var apiUsage = await this.GetUsageFromApiAsync(effectiveApiKey).ConfigureAwait(false);
                if (apiUsage != null)
                {
                    return new[] { apiUsage };
                }
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to get Claude usage from API, falling back to CLI");
            }
        }

        // Fall back to CLI if API fails
        return await this.GetUsageFromCliAsync(config).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets usage information from the OAuth usage endpoint for subscription users.
    /// </summary>
    /// <param name="accessToken">The OAuth access token from credentials file.</param>
    /// <returns>Provider usages if successful, null otherwise.</returns>
    internal async Task<IEnumerable<ProviderUsage>?> GetUsageFromOAuthAsync(string accessToken)
    {
        try
        {
            var (statusCode, responseBody) = await this.SendOAuthRequestAsync(accessToken).ConfigureAwait(false);

            if (statusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                this._logger.LogDebug("OAuth usage endpoint returned 429, retrying once after 2s");
                await Task.Delay(2000).ConfigureAwait(false);
                (statusCode, responseBody) = await this.SendOAuthRequestAsync(accessToken).ConfigureAwait(false);
            }

            if ((int)statusCode < 200 || (int)statusCode >= 300)
            {
                this._logger.LogDebug("OAuth usage endpoint returned {StatusCode}: {Body}", statusCode, responseBody);
                return null;
            }

            var usageResponse = JsonSerializer.Deserialize<OAuthUsageResponse>(responseBody);
            if (usageResponse == null)
            {
                this._logger.LogWarning("Failed to deserialize OAuth usage response");
                return null;
            }

            return this.ParseOAuthUsageResponse(usageResponse, responseBody, (int)statusCode);
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

    private async Task<(System.Net.HttpStatusCode StatusCode, string Body)> SendOAuthRequestAsync(string accessToken)
    {
        var request = CreateBearerRequest(HttpMethod.Get, OAuthUsageEndpoint, accessToken);
        request.Headers.Add("anthropic-beta", OAuthBetaHeader);

        using var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    /// <summary>
    /// Re-reads the OAuth access token from ~/.claude/.credentials.json.
    /// The Claude Code CLI refreshes this file when the token expires.
    /// </summary>
    private string? ReadFreshOAuthToken()
    {
        try
        {
            var credentialsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude",
                ".credentials.json");

            if (!File.Exists(credentialsPath))
            {
                return null;
            }

            var json = File.ReadAllText(credentialsPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                return null;
            }

            if (!oauth.TryGetProperty("accessToken", out var tokenElement))
            {
                return null;
            }

            var token = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            this._logger.LogDebug("Re-read fresh OAuth token from credentials file ({Length} chars)", token.Length);
            return token;
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to re-read OAuth token from credentials file");
            return null;
        }
    }

    private IReadOnlyList<ProviderUsage> ParseOAuthUsageResponse(OAuthUsageResponse response, string rawJson, int httpStatus)
    {
        var results = new List<ProviderUsage>();

        // Current session (5-hour burst quota)
        if (response.FiveHour != null)
        {
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = this.Definition.DisplayName,
                CardId = "current-session",
                GroupId = this.ProviderId,
                Name = "Current Session",
                WindowKind = WindowKind.Burst,
                UsedPercent = UsageMath.ClampPercent(response.FiveHour.Utilization),
                NextResetTime = response.FiveHour.ResetsAt,
                PeriodDuration = TimeSpan.FromHours(5),
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = $"{response.FiveHour.Utilization:F0}% used",
            });
        }

        if (response.SevenDaySonnet != null)
        {
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = this.Definition.DisplayName,
                CardId = "sonnet",
                GroupId = this.ProviderId,
                Name = "Sonnet",
                UsedPercent = UsageMath.ClampPercent(response.SevenDaySonnet.Utilization),
                NextResetTime = response.SevenDay?.ResetsAt,
                PeriodDuration = TimeSpan.FromDays(7),
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = $"{response.SevenDaySonnet.Utilization:F0}% used",
            });
        }

        if (response.SevenDayOpus != null)
        {
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = this.Definition.DisplayName,
                CardId = "opus",
                GroupId = this.ProviderId,
                Name = "Opus",
                UsedPercent = UsageMath.ClampPercent(response.SevenDayOpus.Utilization),
                NextResetTime = response.SevenDay?.ResetsAt,
                PeriodDuration = TimeSpan.FromDays(7),
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = $"{response.SevenDayOpus.Utilization:F0}% used",
            });
        }

        // All-models 7-day rolling quota
        if (response.SevenDay != null)
        {
            var desc = $"5h: {response.FiveHour?.Utilization ?? 0:F0}% | 7d: {response.SevenDay.Utilization:F0}% used";
            if (response.ExtraUsage?.IsEnabled == true)
            {
                desc += " | Extra usage enabled";
            }

            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = this.Definition.DisplayName,
                CardId = "all-models",
                GroupId = this.ProviderId,
                Name = "All Models",
                WindowKind = WindowKind.Rolling,
                UsedPercent = UsageMath.ClampPercent(response.SevenDay.Utilization),
                NextResetTime = response.SevenDay.ResetsAt,
                PeriodDuration = TimeSpan.FromDays(7),
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = desc,
            });
        }

        if (results.Count == 0)
        {
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = this.Definition.DisplayName,
                IsQuotaBased = true,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                RawJson = rawJson,
                HttpStatus = httpStatus,
                Description = "Usage data unavailable",
            });
        }

        return results;
    }

    private async Task<ProviderUsage?> GetUsageFromApiAsync(string apiKey)
    {
        try
        {
            // Make a test request to get rate limit headers
            // Note: Anthropic API doesn't have a usage endpoint, so we use rate limits from headers
            using var testRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
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
                var description = $"Tier: {rateLimitHeaders.GetTierName()} | RPM: {rateLimitHeaders.RequestsRemaining}/{rateLimitHeaders.RequestsLimit} | Tokens/min: {rateLimitHeaders.InputTokensRemaining}/{rateLimitHeaders.InputTokensLimit}";

                return new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    UsedPercent = usagePercentage,
                    RequestsUsed = 0, // Anthropic doesn't provide cost via API
                    RequestsAvailable = 0,
                    IsQuotaBased = false,
                    PlanType = this.Definition.PlanType,
                    IsAvailable = true,
                    Description = description,
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

        if (headers.TryGetValues("anthropic-ratelimit-requests-limit", out var requestLimitValues) &&
            int.TryParse(requestLimitValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var limit))
        {
            info.RequestsLimit = limit;
        }

        if (headers.TryGetValues("anthropic-ratelimit-requests-remaining", out var requestRemainingValues) &&
            int.TryParse(requestRemainingValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var remaining))
        {
            info.RequestsRemaining = remaining;
        }

        if (headers.TryGetValues("anthropic-ratelimit-input-tokens-limit", out var inputLimitValues) &&
            int.TryParse(inputLimitValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var inputLimit))
        {
            info.InputTokensLimit = inputLimit;
        }

        if (headers.TryGetValues("anthropic-ratelimit-input-tokens-remaining", out var inputRemainingValues) &&
            int.TryParse(inputRemainingValues.FirstOrDefault(), CultureInfo.InvariantCulture, out var inputRemaining))
        {
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
                        ProviderName = this.Definition.DisplayName,
                        IsAvailable = true,
                        Description = "Connected (API key configured)",
                        IsStatusOnly = true,
                        IsQuotaBased = false,
                        PlanType = this.Definition.PlanType,
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
                        ProviderName = this.Definition.DisplayName,
                        IsAvailable = true,
                        Description = "Connected (API key configured)",
                        IsStatusOnly = true,
                        IsQuotaBased = false,
                        PlanType = this.Definition.PlanType,
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
                    ProviderName = this.Definition.DisplayName,
                    IsAvailable = true,
                    Description = "Connected (API key configured)",
                    IsStatusOnly = true,
                    IsQuotaBased = false,
                    PlanType = this.Definition.PlanType,
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
        if (remainingMatch.Success && budgetLimit is 0)
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
            ProviderName = this.Definition.DisplayName,
            UsedPercent = Math.Min(usagePercentage, 100),
            RequestsUsed = currentUsage,
            RequestsAvailable = budgetLimit,
            IsCurrencyUsage = true,
            IsQuotaBased = false,
            PlanType = this.Definition.PlanType,
            IsAvailable = true,
            Description = budgetLimit > 0
                ? $"${currentUsage.ToString("F2", CultureInfo.InvariantCulture)} used of ${budgetLimit.ToString("F2", CultureInfo.InvariantCulture)} limit"
                : $"${currentUsage.ToString("F2", CultureInfo.InvariantCulture)} used",
            RawJson = output,
            HttpStatus = 200,
        };
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
}
