// <copyright file="XaiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class XaiProvider : ProviderBase
{
    private const string ApiKeyEndpoint = "https://api.x.ai/v1/api-key";

    private readonly HttpClient _httpClient;
    private readonly ILogger<XaiProvider> _logger;

    public XaiProvider(HttpClient httpClient, ILogger<XaiProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "xai",
        "xAI (Grok)",
        PlanType.Usage,
        isQuotaBased: false)
    {
        DiscoveryEnvironmentVariables = new[] { "XAI_API_KEY" },
        RooConfigPropertyNames = new[] { "xaiApiKey" },
        ExplicitApiKeyPrefixes = new[] { "xai-" },
        IsCurrencyUsage = true,
        BadgeColorHex = "#1a1a2e",
        BadgeInitial = "XAI",
    };

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                this.CreateUnavailableUsage(
                    "API Key missing",
                    state: ProviderUsageState.Missing),
            };
        }

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        var fetchResult = await this.FetchJsonAsync<XaiApiKeyResponse>(
            ApiKeyEndpoint,
            config,
            this._httpClient,
            this._logger,
            cancellationToken)
            .ConfigureAwait(false);

        if (!fetchResult.IsSuccess)
        {
            var errorUsage = fetchResult.FailureUsage!;
            if (errorUsage.HttpStatus > 0)
            {
                errorUsage.IsAvailable = true;
                errorUsage.FailureContext = HttpFailureContext.FromHttpStatus(fetchResult.HttpStatus);
            }

            return new[] { errorUsage };
        }

        var result = fetchResult.Data!;
        var content = fetchResult.RawContent;
        var httpStatus = fetchResult.HttpStatus;

        var usage = this.CreateBaseUsage(providerLabel, content, httpStatus);

        if (result.ApiKeyBlocked || result.TeamBlocked)
        {
            usage.IsAvailable = false;
            usage.Description = result.ApiKeyBlocked ? "API key is blocked" : "Team account is blocked";
            usage.State = ProviderUsageState.Error;
        }
        else if (result.ApiKeyDisabled)
        {
            usage.IsAvailable = false;
            usage.Description = "API key is disabled";
            usage.State = ProviderUsageState.Error;
        }
        else
        {
            usage.Description = string.IsNullOrWhiteSpace(result.Name)
                ? "Active"
                : $"Active — {result.Name}";
        }

        usage.UsedPercent = 0;
        usage.RequestsUsed = 0;
        usage.RequestsAvailable = 0;

        return new[] { usage };
    }

    private sealed class XaiApiKeyResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("api_key_blocked")]
        public bool ApiKeyBlocked { get; set; }

        [JsonPropertyName("api_key_disabled")]
        public bool ApiKeyDisabled { get; set; }

        [JsonPropertyName("team_blocked")]
        public bool TeamBlocked { get; set; }

        [JsonPropertyName("redacted_api_key")]
        public string? RedactedApiKey { get; set; }

        [JsonPropertyName("team_id")]
        public string? TeamId { get; set; }
    }
}
