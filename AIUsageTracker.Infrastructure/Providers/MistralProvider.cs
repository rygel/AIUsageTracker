// <copyright file="MistralProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>


using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class MistralProvider : ProviderBase
{
    private readonly IResilientHttpClient _resilientHttpClient;
    private readonly ILogger<MistralProvider> _logger;

    public MistralProvider(IResilientHttpClient resilientHttpClient, ILogger<MistralProvider> logger, IProviderDiscoveryService? discoveryService = null)
        : base(discoveryService)
    {
        this._resilientHttpClient = resilientHttpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "mistral",
        "Mistral",
        PlanType.Usage,
        isQuotaBased: false,
        defaultConfigType: "pay-as-you-go")
    {
        RooConfigPropertyNames = new[] { "mistralApiKey" },
        IconAssetName = "mistral",
        FallbackBadgeColorHex = "#FF4500",
        FallbackBadgeInitial = "Mi",
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
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
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.mistral.ai/v1/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await this._resilientHttpClient.SendAsync(request, this.ProviderId).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new[]
                {
                    new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    IsAvailable = true,
                    UsedPercent = 0,
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    Description = "Connected (Check Dashboard)",
                    IsStatusOnly = true,
                    RawJson = content,
                    HttpStatus = (int)response.StatusCode,
                },
                };
            }
            else
            {
                return new[] { this.CreateUnavailableUsageFromStatus(response) };
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to verify Mistral API key");
            return new[] { this.CreateUnavailableUsageFromException(ex, "Failed to verify Mistral API key") };
        }
    }
}
