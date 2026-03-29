// <copyright file="ZaiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class ZaiProvider : ProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ZaiProvider> _logger;

    public ZaiProvider(HttpClient httpClient, ILogger<ZaiProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "zai-coding-plan",
        "Z.ai Coding Plan",
        PlanType.Coding,
        isQuotaBased: true)
    {
        AdditionalHandledProviderIds = new[] { "zai" },
        DisplayNameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["zai"] = "Z.AI",
        },
        DiscoveryEnvironmentVariables = new[] { "ZAI_API_KEY", "Z_AI_API_KEY" },
        RooConfigPropertyNames = new[] { "zaiApiKey" },
        IconAssetName = "zai",
        BadgeColorHex = "#20B2AA",
        BadgeInitial = "Z",
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ArgumentException("API Key not found for Z.AI provider.", nameof(config));
        }

        var providerLabel = this.Definition.DisplayName;

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.z.ai/api/monitor/usage/quota/limit");

        // Z.AI uses raw key in Authorization header without "Bearer" prefix based on Swift ref
        request.Headers.TryAddWithoutValidation("Authorization", config.ApiKey);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en");

        this._logger.LogDebug("[ZAI] Sending API request to https://api.z.ai/api/monitor/usage/quota/limit");
        var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            this._logger.LogError("[ZAI] API returned {StatusCode}", response.StatusCode);
            throw new Exception($"Z.AI API returned {response.StatusCode}");
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        this._logger.LogDebug("[ZAI RAW RESPONSE] {Response}", responseString);

        // Parse envelope
        var envelope = JsonSerializer.Deserialize<ZaiEnvelope<ZaiQuotaLimitResponse>>(responseString);
        this._logger.LogDebug("[ZAI] Parsed envelope - Data is null: {IsNull}", envelope?.Data == null);

        string planDescription = "API";

        var limits = envelope?.Data?.Limits;
        this._logger.LogDebug("[ZAI] Limits count: {Count}", limits?.Count ?? 0);

        if (limits == null || !limits.Any())
        {
            this._logger.LogDebug("[ZAI] No limits found in response");
            return new[]
            {
                new ProviderUsage
             {
                 ProviderId = this.ProviderId,
                 ProviderName = providerLabel,
                 IsAvailable = false,
                 Description = "No usage data available",
                 IsQuotaBased = this.Definition.IsQuotaBased,
                 PlanType = this.Definition.PlanType,
                 RawJson = responseString,
                 HttpStatus = httpStatus,
             },
            };
        }

        // Log all limits for debugging
        foreach (var limit in limits)
        {
            this._logger.LogDebug(
                "[ZAI LIMIT] Type={Type} Total={Total} CurrentValue={Current} Remaining={Remaining} Percentage={Pct} ResetTime={Ts}",
                limit.Type,
                limit.Total,
                limit.CurrentValue,
                limit.Remaining,
                limit.Percentage,
                limit.NextResetTime);
        }

        // Helper to check if a limit is in the future (or has no expiry)
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        bool IsFuture(long? resetTime)
        {
            if (!resetTime.HasValue || resetTime.Value == 0)
            {
                return true; // No reset time = always active
            }

            var ts = resetTime.Value;

            // Heuristic to detect milliseconds vs seconds (similar to logic below)
            if (ts > 10000000000)
            {
                return ts > nowMs;
            }

            return ts > nowSec;
        }

        var tokenLimits = limits.Where(l =>
            l.Type != null && (l.Type.Equals("TOKENS_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                               l.Type.Equals("Tokens", StringComparison.OrdinalIgnoreCase))).ToList();

        // Prefer active limits (future reset time or no reset time)
        // If multiple active limits exist, pick the specific one that is most restrictive (lowest remaining)
        // If no active limits, fall back to any limit (historical)
        var tokenLimit = tokenLimits.Where(l => IsFuture(l.NextResetTime))
                                    .OrderBy(l => l.Remaining ?? long.MaxValue)
                                    .FirstOrDefault()
                         ?? tokenLimits.FirstOrDefault();

        var mcpLimit = limits.FirstOrDefault(l =>
            l.Type != null && (l.Type.Equals("TIME_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                               l.Type.Equals("Time", StringComparison.OrdinalIgnoreCase)));

        this._logger.LogDebug(
            "[ZAI] Found token limit: {Found}, mcp limit: {McpFound}",
            tokenLimit != null,
            mcpLimit != null);

        double? remainingPercent = null;
        string detailInfo = string.Empty;

        // Define variables to hold real values if available, otherwise default to percentage logic
        double finalRequestsAvailable = 100;
        double finalRequestsUsedReal = 0;
        var hasRawLimitData = false;

        if (tokenLimit != null)
        {
            planDescription = "Coding Plan";
            this._logger.LogDebug(
                "[ZAI] Processing TOKENS_LIMIT - Percentage={Pct} Total={Total} Current={Current} Remaining={Remaining}",
                tokenLimit.Percentage,
                tokenLimit.Total,
                tokenLimit.CurrentValue,
                tokenLimit.Remaining);

            if (tokenLimit.Percentage.HasValue && tokenLimit.Total == null && tokenLimit.CurrentValue == null && tokenLimit.Remaining == null)
            {
                double usedPercent = tokenLimit.Percentage.Value;
                double remainingPercentVal = 100 - usedPercent;
                remainingPercent = remainingPercent.HasValue
                    ? Math.Min(remainingPercent.Value, remainingPercentVal)
                    : remainingPercentVal;
                detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining";
                this._logger.LogDebug("[ZAI] Percentage-only mode: {Used}% used, {Remaining}% remaining", usedPercent, remainingPercentVal);

                finalRequestsUsedReal = 100 - remainingPercentVal;
            }
            else
            {
                double totalVal = tokenLimit.Total ?? 0;
                double usedVal = tokenLimit.CurrentValue ?? 0;
                double remainingVal = tokenLimit.Remaining ?? (totalVal - usedVal);

                if (tokenLimit.Total > 50000000)
                {
                    planDescription = "Coding Plan (Ultra/Enterprise)";
                }
                else if (tokenLimit.Total > 10000000)
                {
                    planDescription = "Coding Plan (Pro)";
                }

                if (totalVal > 0)
                {
                    double remainingPercentVal = (remainingVal / totalVal) * 100.0;

                    this._logger.LogDebug(
                        "[ZAI] Calculation: Remaining={RemainingVal} / Total={TotalVal} = {Percent}%",
                        remainingVal,
                        totalVal,
                        remainingPercentVal);

                    remainingPercent = remainingPercent.HasValue
                        ? Math.Min(remainingPercent.Value, remainingPercentVal)
                        : remainingPercentVal;

                    // Expose raw values for UI to display "X / Y used"
                    finalRequestsAvailable = totalVal;
                    finalRequestsUsedReal = usedVal;
                    hasRawLimitData = true;
                    detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining of {(totalVal / 1000000.0).ToString("F0", CultureInfo.InvariantCulture)}M tokens limit";
                }
                else if (tokenLimit.Percentage.HasValue)
                {
                    double usedPercent = tokenLimit.Percentage.Value;
                    double remainingPercentVal = 100 - usedPercent;
                    remainingPercent = remainingPercent.HasValue
                        ? Math.Min(remainingPercent.Value, remainingPercentVal)
                        : remainingPercentVal;
                    detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining";
                    finalRequestsUsedReal = 100 - remainingPercentVal;
                }
                else
                {
                    this._logger.LogDebug("[ZAI] Token limit missing usable quota metrics; usage unknown");
                }
            }
        }

        if (mcpLimit != null && mcpLimit.Percentage > 0)
        {
            this._logger.LogDebug("[ZAI] Processing TIME_LIMIT - Percentage: {Percentage}", mcpLimit.Percentage);
            double mcpRemainingPercent = Math.Max(0, 100 - mcpLimit.Percentage.Value);
            remainingPercent = remainingPercent.HasValue
                ? Math.Min(remainingPercent.Value, mcpRemainingPercent)
                : mcpRemainingPercent;
            this._logger.LogDebug("[ZAI] MCP remaining percent: {McpRemainingPercent}", mcpRemainingPercent);
        }

        // Compute the token window duration from unit/number.
        // Z.ai encodes: unit=3 → hours, number=N → N-hour rolling window.
        // Verified from Coding Plan: unit=3, number=5 → 5-hour rolling window.
        var tokenWindowDuration = tokenLimit?.Unit == 3 && tokenLimit.Number.HasValue
            ? TimeSpan.FromHours(tokenLimit.Number.Value)
            : (TimeSpan?)null;
        var tokenWindowLabel = tokenWindowDuration.HasValue
            ? $"{(int)tokenWindowDuration.Value.TotalHours}h window"
            : null;

        DateTime? nextResetTime = null;
        string resetStr = string.Empty;

        if (tokenLimit != null && tokenLimit.Percentage > 0 && tokenLimit.NextResetTime.HasValue && tokenLimit.NextResetTime.Value > 0)
        {
            // Active window: the API returns the current window's close time — use it.
            var ts = tokenLimit.NextResetTime.Value;
            this._logger.LogDebug("[ZAI] Active token window reset timestamp: {Ts}", ts);
            nextResetTime = ParseTimestamp(ts);
            resetStr = $" (Resets: {nextResetTime:MMM dd, yyyy HH:mm} Local)";
        }
        else if (tokenWindowLabel != null)
        {
            // Fresh (0% used): the API returns the billing period end, not the rolling window close.
            // Show the window size label instead of a misleading 7-day countdown.
            resetStr = $" ({tokenWindowLabel})";
        }
        else
        {
            // Fallback: use the nearest nextResetTime from any limit.
            var limitWithReset = limits
                .Where(l => l.NextResetTime.HasValue && l.NextResetTime.Value > 0)
                .OrderBy(l => l.NextResetTime!.Value)
                .FirstOrDefault();
            if (limitWithReset != null)
            {
                this._logger.LogDebug("[ZAI] Fallback reset timestamp: {Ts}", limitWithReset.NextResetTime!.Value);
                nextResetTime = ParseTimestamp(limitWithReset.NextResetTime!.Value);
                resetStr = $" (Resets: {nextResetTime:MMM dd, yyyy HH:mm} Local)";
            }
        }

        if (!remainingPercent.HasValue)
        {
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                IsAvailable = false,
                Description = FormatDescription("Usage unknown (missing quota metrics)", planDescription),
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                RawJson = responseString,
                HttpStatus = httpStatus,
            },
            };
        }

        var finalRemainingPercent = Math.Min(remainingPercent.Value, 100);
        var finalUsedPercent = Math.Max(0, 100.0 - finalRemainingPercent);

        if (!hasRawLimitData)
        {
            finalRequestsAvailable = 100;
            finalRequestsUsedReal = Math.Max(0, 100 - finalRemainingPercent);
        }

        var finalDescription = (string.IsNullOrEmpty(detailInfo) ? $"{finalRemainingPercent.ToString("F1", CultureInfo.InvariantCulture)}% remaining" : detailInfo) + resetStr;

        this._logger.LogInformation(
            "Z.AI Provider Usage - ProviderId: {ProviderId}, ProviderName: {ProviderName}, UsedPercent: {UsedPercent}%, RequestsUsed: {RequestsUsed}%, Description: {Description}, IsAvailable: {IsAvailable}",
            this.ProviderId,
            providerLabel,
            finalUsedPercent,
            finalRequestsUsedReal,
            finalDescription,
            true);

        return new[]
        {
            new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = providerLabel,
            UsedPercent = finalUsedPercent,
            RequestsUsed = finalRequestsUsedReal,  // Store actual used count/percentage
            RequestsAvailable = finalRequestsAvailable, // Store actual total limit
            IsQuotaBased = this.Definition.IsQuotaBased,
            PlanType = this.Definition.PlanType,
            DisplayAsFraction = finalRequestsAvailable > 100, // Explicitly request fraction display if we have real numbers
            Description = FormatDescription(finalDescription, planDescription),
            NextResetTime = nextResetTime,
            IsAvailable = true,
            RawJson = responseString,
            HttpStatus = httpStatus,
        },
        };
    }

    private static string FormatDescription(string description, string planDescription)
    {
        return string.Equals(planDescription, "API", StringComparison.OrdinalIgnoreCase)
            ? description
            : $"{description} | Plan: {planDescription}";
    }

    private static DateTime ParseTimestamp(long ts)
    {
        // Heuristic: values > 10^10 are milliseconds (e.g. 1773454046559), otherwise seconds.
        return ts < 10_000_000_000
            ? DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime
            : DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
    }

    private class ZaiEnvelope<T>
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    private class ZaiQuotaLimitResponse
    {
        [JsonPropertyName("limits")]
        public List<ZaiQuotaLimitItem>? Limits { get; set; }
    }

    private class ZaiQuotaLimitItem
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("percentage")]
        public double? Percentage { get; set; }

        [JsonPropertyName("currentValue")]
        public long? CurrentValue { get; set; }

        [JsonPropertyName("usage")] // This is actually the total limit in Z.AI response
        public long? Total { get; set; }

        [JsonPropertyName("remaining")]
        public long? Remaining { get; set; }

        [JsonPropertyName("nextResetTime")]
        public long? NextResetTime { get; set; }

        /// <summary>
        /// Gets or sets z.ai window duration unit. Observed values: 3 = hours, 5 = months.
        /// Coding Plan TOKENS_LIMIT: unit=3, number=5 → 5-hour rolling window.
        /// </summary>
        [JsonPropertyName("unit")]
        public int? Unit { get; set; }

        /// <summary>Gets or sets number of units in the window duration (e.g. 5 for a 5-hour window).</summary>
        [JsonPropertyName("number")]
        public long? Number { get; set; }
    }
}
