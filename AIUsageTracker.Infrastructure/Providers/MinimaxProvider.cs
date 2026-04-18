// <copyright file="MinimaxProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Constants;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class MinimaxProvider : ProviderBase
{
    public const string ChinaProviderId = "minimax";
    public const string InternationalProviderId = "minimax-io";
    public const string InternationalLegacyProviderId = "minimax-global";
    public const string CodingPlanProviderId = "minimax-coding-plan";
    private const string TextGenerationLabel = "text generation";
    private const string TextGenerationModelPrefix = "minimax-text";

    private readonly HttpClient _httpClient;
    private readonly ILogger<MinimaxProvider> _logger;

    public MinimaxProvider(HttpClient httpClient, ILogger<MinimaxProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        ChinaProviderId,
        "MiniMax.io",
        PlanType.Coding,
        isQuotaBased: true)
    {
        AdditionalHandledProviderIds = new[] { InternationalProviderId, InternationalLegacyProviderId, CodingPlanProviderId },
        DisplayNameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [InternationalProviderId] = "MiniMax.io",
            [InternationalLegacyProviderId] = "MiniMax.io",
            [CodingPlanProviderId] = "Minimax.io Coding Plan",
        },
        SettingsAdditionalProviderIds = new[] { InternationalProviderId, CodingPlanProviderId },
        DiscoveryEnvironmentVariables = new[] { "MINIMAX_API_KEY" },
        IconAssetName = "minimax",
        BadgeColorHex = "#00CED1",
        BadgeInitial = "MM",
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = "API Key not found.",
                    State = ProviderUsageState.Missing,
                },
            };
        }

        if (string.Equals(config.ProviderId, CodingPlanProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return await this.GetCodingPlanUsageAsync(config, providerLabel, cancellationToken).ConfigureAwait(false);
        }

        return await this.GetTokenUsageAsync(config, providerLabel, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IEnumerable<ProviderUsage>> GetTokenUsageAsync(
        ProviderConfig config,
        string providerLabel,
        CancellationToken cancellationToken)
    {
        string url;
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
            url = string.Equals(config.ProviderId, InternationalProviderId, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(config.ProviderId, InternationalLegacyProviderId, StringComparison.OrdinalIgnoreCase)
                ? ProviderEndpoints.Minimax.UserUsage
                : ProviderEndpoints.Minimax.ChatUserUsage;
        }

        var request = CreateBearerRequest(HttpMethod.Get, url, config.ApiKey);
        var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = $"API returned {response.StatusCode} for {url}",
                    RawJson = errorContent,
                    HttpStatus = httpStatus,
                },
            };
        }

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var minimax = JsonSerializer.Deserialize<MinimaxTokenResponse>(responseString);
            if (minimax?.Usage == null)
            {
                return new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = providerLabel,
                        IsAvailable = false,
                        IsQuotaBased = this.Definition.IsQuotaBased,
                        PlanType = this.Definition.PlanType,
                        Description = "Invalid Minimax response format",
                        RawJson = responseString,
                        HttpStatus = httpStatus,
                    },
                };
            }

            var used = minimax.Usage.TokensUsed;
            var total = minimax.Usage.TokensLimit > 0 ? minimax.Usage.TokensLimit : 0;
            var utilization = total > 0 ? (used / total) * 100.0 : 0;

            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    UsedPercent = Math.Clamp(utilization, 0, 100),
                    RequestsUsed = used,
                    RequestsAvailable = total,
                    PlanType = this.Definition.PlanType,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    Description = $"{used.ToString("N0", CultureInfo.InvariantCulture)} tokens used" + (total > 0 ? $" / {total.ToString("N0", CultureInfo.InvariantCulture)} limit" : string.Empty),
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }
        catch (JsonException ex)
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = $"Failed to parse Minimax response: {ex.Message}",
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }
    }

    private async Task<IEnumerable<ProviderUsage>> GetCodingPlanUsageAsync(
        ProviderConfig config,
        string providerLabel,
        CancellationToken cancellationToken)
    {
        string url;
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            url = config.BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? config.BaseUrl
                : "https://" + config.BaseUrl;
        }
        else
        {
            url = ProviderEndpoints.Minimax.CodingPlanRemains;
        }

        var request = CreateBearerRequest(HttpMethod.Get, url, config.ApiKey);
        var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;
        var responseString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = $"API returned {response.StatusCode}",
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }

        try
        {
            var codingPlan = JsonSerializer.Deserialize<MinimaxCodingPlanResponse>(responseString);
            if (codingPlan?.BaseResp?.StatusCode != 0 || codingPlan.ModelRemains == null || codingPlan.ModelRemains.Count == 0)
            {
                var errMsg = codingPlan?.BaseResp?.StatusMsg ?? "Invalid MiniMax Coding Plan response";
                return new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = providerLabel,
                        IsAvailable = false,
                        IsQuotaBased = this.Definition.IsQuotaBased,
                        PlanType = this.Definition.PlanType,
                        Description = errMsg,
                        RawJson = responseString,
                        HttpStatus = httpStatus,
                    },
                };
            }

            var textGenerationModel = FindTextGenerationModel(codingPlan.ModelRemains);
            if (textGenerationModel == null)
            {
                return new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = providerLabel,
                        IsAvailable = false,
                        IsQuotaBased = this.Definition.IsQuotaBased,
                        PlanType = this.Definition.PlanType,
                        Description = "MiniMax Text Generation model missing in coding plan response",
                        RawJson = responseString,
                        HttpStatus = httpStatus,
                    },
                };
            }

            return BuildCodingPlanUsages(config.ProviderId, providerLabel, textGenerationModel, responseString, httpStatus);
        }
        catch (JsonException ex)
        {
            this._logger.LogWarning(ex, "Failed to parse MiniMax Coding Plan response");
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = $"Failed to parse MiniMax Coding Plan response: {ex.Message}",
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }
    }

    private static List<ProviderUsage> BuildCodingPlanUsages(
        string providerId,
        string providerLabel,
        MinimaxModelRemains model,
        string rawJson,
        int httpStatus)
    {
        var usages = new List<ProviderUsage>();

        var modelName = model.ModelName ?? "Text Generation";
        var modelSlug = modelName.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
        const int modelIndex = 0;

        if (model.IntervalTotal > 0)
        {
            usages.Add(BuildModelWindowCard(new ModelWindowCardSpec(
                providerId,
                providerLabel,
                modelName,
                modelSlug,
                modelIndex,
                model.IntervalTotal,
                model.IntervalRemaining,
                model.IntervalEndMs,
                "burst",
                "5h",
                WindowKind.Burst,
                TimeSpan.FromHours(5),
                rawJson,
                httpStatus)));
        }

        if (model.WeeklyTotal > 0)
        {
            usages.Add(BuildModelWindowCard(new ModelWindowCardSpec(
                providerId,
                providerLabel,
                modelName,
                modelSlug,
                modelIndex,
                model.WeeklyTotal,
                model.WeeklyRemaining,
                model.WeeklyEndMs,
                "weekly",
                "Weekly",
                WindowKind.Rolling,
                TimeSpan.FromDays(7),
                rawJson,
                httpStatus)));
        }

        return usages;
    }

    private static MinimaxModelRemains? FindTextGenerationModel(
        IReadOnlyList<MinimaxModelRemains> modelRemains)
    {
        return modelRemains.FirstOrDefault(model =>
            !string.IsNullOrWhiteSpace(model.ModelName) &&
            (model.ModelName.Contains(TextGenerationLabel, StringComparison.OrdinalIgnoreCase) ||
             model.ModelName.Contains(TextGenerationModelPrefix, StringComparison.OrdinalIgnoreCase)));
    }

    private static ProviderUsage BuildModelWindowCard(ModelWindowCardSpec spec)
    {
        var remaining = Math.Max(0, Math.Min(spec.WindowRemaining, spec.WindowTotal));
        var used = spec.WindowTotal - remaining;
        var usedPct = Math.Clamp((used / spec.WindowTotal) * 100.0, 0, 100);
        var resetTime = spec.ResetMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(spec.ResetMs).UtcDateTime
            : (DateTime?)null;

        return new ProviderUsage
        {
            ProviderId = spec.ProviderId,
            ProviderName = spec.ProviderLabel,
            CardId = spec.ModelIndex == 0 ? spec.CardSuffix : $"{spec.ModelSlug}.{spec.CardSuffix}",
            GroupId = spec.ProviderId,
            Name = spec.ModelIndex == 0 ? spec.NameSuffix : $"{spec.ModelName} {spec.NameSuffix}",
            WindowKind = spec.WindowKind,
            UsedPercent = usedPct,
            RequestsUsed = used,
            RequestsAvailable = spec.WindowTotal,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            IsAvailable = true,
            Description = $"{Math.Clamp(100.0 - usedPct, 0, 100).ToString("F0", CultureInfo.InvariantCulture)}% remaining ({spec.ModelName})",
            NextResetTime = resetTime,
            PeriodDuration = spec.PeriodDuration,
            RawJson = spec.RawJson,
            HttpStatus = spec.HttpStatus,
        };
    }

    private readonly record struct ModelWindowCardSpec(
        string ProviderId,
        string ProviderLabel,
        string ModelName,
        string ModelSlug,
        int ModelIndex,
        double WindowTotal,
        double WindowRemaining,
        long ResetMs,
        string CardSuffix,
        string NameSuffix,
        WindowKind WindowKind,
        TimeSpan PeriodDuration,
        string RawJson,
        int HttpStatus);

    private sealed class MinimaxTokenResponse
    {
        [JsonPropertyName("usage")]
        public MinimaxTokenUsage? Usage { get; set; }
    }

    private sealed class MinimaxTokenUsage
    {
        [JsonPropertyName("tokens_used")]
        public double TokensUsed { get; set; }

        [JsonPropertyName("tokens_limit")]
        public double TokensLimit { get; set; }
    }

    private sealed class MinimaxCodingPlanResponse
    {
        [JsonPropertyName("base_resp")]
        public MinimaxBaseResp? BaseResp { get; set; }

        [JsonPropertyName("model_remains")]
        public List<MinimaxModelRemains>? ModelRemains { get; set; }
    }

    private sealed class MinimaxBaseResp
    {
        [JsonPropertyName("status_code")]
        public int StatusCode { get; set; }

        [JsonPropertyName("status_msg")]
        public string? StatusMsg { get; set; }
    }

    private sealed class MinimaxModelRemains
    {
        [JsonPropertyName("model_name")]
        public string? ModelName { get; set; }

        [JsonPropertyName("current_interval_total_count")]
        public double IntervalTotal { get; set; }

        /// <summary>Gets or sets remaining count for the 5h window (misleadingly named "usage" in the API).</summary>
        [JsonPropertyName("current_interval_usage_count")]
        public double IntervalRemaining { get; set; }

        [JsonPropertyName("end_time")]
        public long IntervalEndMs { get; set; }

        [JsonPropertyName("current_weekly_total_count")]
        public double WeeklyTotal { get; set; }

        /// <summary>Gets or sets remaining count for the weekly window (misleadingly named "usage" in the API).</summary>
        [JsonPropertyName("current_weekly_usage_count")]
        public double WeeklyRemaining { get; set; }

        [JsonPropertyName("weekly_end_time")]
        public long WeeklyEndMs { get; set; }
    }
}
