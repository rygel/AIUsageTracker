// <copyright file="MistralProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class MistralProvider : ProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MistralProvider> _logger;

    public MistralProvider(HttpClient httpClient, ILogger<MistralProvider> logger, IProviderDiscoveryService? discoveryService = null)
        : base(discoveryService)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "mistral",
        "Mistral",
        PlanType.Usage,
        isQuotaBased: false)
    {
        RooConfigPropertyNames = new[] { "mistralApiKey" },
        IsStatusOnly = true,
        IconAssetName = "mistral",
        BadgeColorHex = "#FF4500",
        BadgeInitial = "Mi",
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        var apiKey = config.ApiKey;

        if (string.IsNullOrEmpty(apiKey) && this.DiscoveryService != null)
        {
            apiKey = this.DiscoveryService.GetEnvironmentVariable("MISTRAL_API_KEY");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return new[] { this.CreateUnavailableUsage("API Key missing", state: ProviderUsageState.Missing) };
        }

        // Mistral does not have a public usage/billing API endpoint
        // We verify the key works by calling the models list endpoint
        try
        {
            var request = CreateBearerRequest(HttpMethod.Get, "https://api.mistral.ai/v1/models", apiKey);

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = this.ProviderId,
                        ProviderName = this.Definition.DisplayName,
                        IsAvailable = true,
                        UsedPercent = 0,
                        IsQuotaBased = this.Definition.IsQuotaBased,
                        PlanType = this.Definition.PlanType,
                        Description = "Connected (Check Dashboard)",
                        RawJson = content,
                        HttpStatus = (int)response.StatusCode,
                    },
                }
                : new[] { this.CreateUnavailableUsageFromStatus(response) };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to verify Mistral API key");
            return new[] { this.CreateUnavailableUsageFromException(ex, "Failed to verify Mistral API key") };
        }
    }
}
