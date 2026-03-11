// <copyright file="MinimaxProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class MinimaxProvider : ProviderBase
{
    public const string ChinaProviderId = "minimax";
    public const string InternationalProviderId = "minimax-io";
    public const string InternationalLegacyProviderId = "minimax-global";

    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: ChinaProviderId,
        displayName: "Minimax (China)",
        planType: PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based",
        includeInWellKnownProviders: true,
        handledProviderIds: new[] { ChinaProviderId, InternationalProviderId, InternationalLegacyProviderId },
        displayNameOverrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [InternationalProviderId] = "Minimax (International)",
            [InternationalLegacyProviderId] = "Minimax (International)",
        },
        discoveryEnvironmentVariables: new[] { "MINIMAX_API_KEY" },
        iconAssetName: "minimax",
        fallbackBadgeColorHex: "#00CED1",
        fallbackBadgeInitial: "MM");

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MinimaxProvider> _logger;

    public MinimaxProvider(HttpClient httpClient, ILogger<MinimaxProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Minimax",
                IsAvailable = false,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Description = "API Key not found."
            },
            };
        }

        string url;

        // Prioritize BaseUrl if set
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            url = config.BaseUrl;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }
        }
        else
        {
            // Determine endpoint based on ID suffix
            if (string.Equals(config.ProviderId, InternationalProviderId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(config.ProviderId, InternationalLegacyProviderId, StringComparison.OrdinalIgnoreCase))
            {
                url = "https://api.minimax.io/v1/user/usage";
            }
            else
            {
                url = "https://api.minimax.chat/v1/user/usage";
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Minimax",
                IsAvailable = false,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Description = $"API returned {response.StatusCode} for {url}",
                RawJson = errorContent,
                HttpStatus = httpStatus
            },
            };
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        double used = 0;
        double total = 0;

        try
        {
            var minimax = JsonSerializer.Deserialize<MinimaxResponse>(responseString);
            if (minimax?.Usage != null)
            {
                used = minimax.Usage.TokensUsed;
                total = minimax.Usage.TokensLimit > 0 ? minimax.Usage.TokensLimit : 0;
            }
            else
            {
                return new[]
                {
                    new ProviderUsage
             {
                 ProviderId = config.ProviderId,
                 ProviderName = "Minimax",
                 IsAvailable = false,
                 IsQuotaBased = true,
                 PlanType = PlanType.Coding,
                 Description = "Invalid Minimax response format",
                 RawJson = responseString,
                 HttpStatus = httpStatus
             },
                };
            }
        }
        catch (JsonException ex)
        {
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Minimax",
                IsAvailable = false,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Description = $"Failed to parse Minimax response: {ex.Message}",
                RawJson = responseString,
                HttpStatus = httpStatus
            },
            };
        }

        var utilization = total > 0 ? (used / total) * 100.0 : 0;

        return new[]
        {
            new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = "Minimax",
            RequestsPercentage = Math.Min(utilization, 100),
            RequestsUsed = used,
            RequestsAvailable = total,
            PlanType = PlanType.Coding,
            UsageUnit = "Tokens",
            IsQuotaBased = true,
            Description = $"{used:N0} tokens used" + (total > 0 ? $" / {total:N0} limit" : string.Empty),
            RawJson = responseString,
            HttpStatus = httpStatus
        },
        };
    }

    private class MinimaxResponse
    {
        [JsonPropertyName("usage")]
        public MinimaxUsage? Usage { get; set; }
    }

    private class MinimaxUsage
    {
        [JsonPropertyName("tokens_used")]
        public double TokensUsed { get; set; }

        [JsonPropertyName("tokens_limit")]
        public double TokensLimit { get; set; }
    }
}
