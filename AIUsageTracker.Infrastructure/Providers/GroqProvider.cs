// <copyright file="GroqProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http.Headers;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class GroqProvider : ProviderBase
{
    private const string ModelsEndpoint = "https://api.groq.com/openai/v1/models";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqProvider> _logger;

    public GroqProvider(HttpClient httpClient, ILogger<GroqProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "groq",
        "Groq",
        PlanType.Usage,
        isQuotaBased: false)
    {
        DiscoveryEnvironmentVariables = new[] { "GROQ_API_KEY" },
        BadgeColorHex = "#F55036",
        BadgeInitial = "G",
    };

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                this.CreateUnavailableUsage("API Key missing", state: ProviderUsageState.Missing),
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

            using var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var httpStatus = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogWarning("Groq API error: {StatusCode}", response.StatusCode);
                var errorUsage = this.CreateUnavailableUsage(
                    DescribeUnavailableStatus(response.StatusCode),
                    httpStatus,
                    authSource: config.AuthSource);
                errorUsage.FailureContext = HttpFailureContext.FromHttpStatus(httpStatus);
                return new[] { errorUsage };
            }

            var limitRequests = TryParseHeaderDouble(response.Headers, "x-ratelimit-limit-requests");
            var remainingRequests = TryParseHeaderDouble(response.Headers, "x-ratelimit-remaining-requests");
            var resetRequestsSeconds = TryParseHeaderDouble(response.Headers, "x-ratelimit-reset-requests");
            var limitTokens = TryParseHeaderDouble(response.Headers, "x-ratelimit-limit-tokens");
            var remainingTokens = TryParseHeaderDouble(response.Headers, "x-ratelimit-remaining-tokens");
            var resetTokensSeconds = TryParseHeaderDouble(response.Headers, "x-ratelimit-reset-tokens");

            var cards = new List<ProviderUsage>();

            if (limitRequests.HasValue && remainingRequests.HasValue)
            {
                var usedRequests = limitRequests.Value - remainingRequests.Value;
                var usedPercent = limitRequests.Value > 0
                    ? UsageMath.ClampPercent(usedRequests / limitRequests.Value * 100.0)
                    : 0;
                var resetTime = ResolveResetTimeFromSeconds(resetRequestsSeconds);
                var resetDesc = FormatResetDescription(resetRequestsSeconds);

                cards.Add(new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    Name = "Daily Requests",
                    CardId = "daily-requests",
                    GroupId = this.ProviderId,
                    IsAvailable = true,
                    UsedPercent = usedPercent,
                    RequestsUsed = usedRequests,
                    RequestsAvailable = limitRequests.Value,
                    DisplayAsFraction = true,
                    PlanType = this.Definition.PlanType,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    Description = $"{remainingRequests.Value.ToString("F0", CultureInfo.InvariantCulture)} / {limitRequests.Value.ToString("F0", CultureInfo.InvariantCulture)} requests remaining{(string.IsNullOrEmpty(resetDesc) ? string.Empty : " | " + resetDesc)}",
                    NextResetTime = resetTime,
                    RawJson = content,
                    HttpStatus = httpStatus,
                });
            }

            if (limitTokens.HasValue && remainingTokens.HasValue)
            {
                var usedTokens = limitTokens.Value - remainingTokens.Value;
                var usedPercent = limitTokens.Value > 0
                    ? UsageMath.ClampPercent(usedTokens / limitTokens.Value * 100.0)
                    : 0;
                var resetTime = ResolveResetTimeFromSeconds(resetTokensSeconds);
                var resetDesc = FormatResetDescription(resetTokensSeconds);

                cards.Add(new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    Name = "Per-Minute Tokens",
                    CardId = "per-minute-tokens",
                    GroupId = this.ProviderId,
                    IsAvailable = true,
                    UsedPercent = usedPercent,
                    RequestsUsed = usedTokens,
                    RequestsAvailable = limitTokens.Value,
                    DisplayAsFraction = true,
                    PlanType = this.Definition.PlanType,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    Description = $"{remainingTokens.Value.ToString("F0", CultureInfo.InvariantCulture)} / {limitTokens.Value.ToString("F0", CultureInfo.InvariantCulture)} tokens remaining{(string.IsNullOrEmpty(resetDesc) ? string.Empty : " | " + resetDesc)}",
                    NextResetTime = resetTime,
                    RawJson = content,
                    HttpStatus = httpStatus,
                });
            }

            if (cards.Count == 0)
            {
                cards.Add(new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = true,
                    IsStatusOnly = true,
                    UsedPercent = 0,
                    PlanType = this.Definition.PlanType,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    Description = "Connected (no rate-limit headers returned)",
                    RawJson = content,
                    HttpStatus = httpStatus,
                });
            }

            return cards;
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogError(ex, "Groq API key validation failed");
            return new[]
            {
                this.CreateUnavailableUsage(
                    "Connection failed - check network",
                    authSource: config.AuthSource,
                    failureContext: HttpFailureContext.FromException(ex, HttpFailureClassification.Network)),
            };
        }
        catch (TaskCanceledException ex)
        {
            this._logger.LogError(ex, "Groq API key validation timed out");
            return new[]
            {
                this.CreateUnavailableUsage(
                    "Request timed out",
                    authSource: config.AuthSource,
                    failureContext: HttpFailureContext.FromException(ex, HttpFailureClassification.Timeout)),
            };
        }
    }

    private static double? TryParseHeaderDouble(HttpResponseHeaders headers, string name)
    {
        if (headers.TryGetValues(name, out var values))
        {
            var raw = values.FirstOrDefault();
            if (raw != null && double.TryParse(raw, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        return null;
    }
}
