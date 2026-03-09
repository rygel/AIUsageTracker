using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.Infrastructure.Providers;

public class MistralProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "mistral",
        displayName: "Mistral",
        planType: PlanType.Usage,
        isQuotaBased: false,
        defaultConfigType: "pay-as-you-go",
        rooConfigPropertyNames: new[] { "mistralApiKey" },
        iconAssetName: "mistral",
        fallbackBadgeColorHex: "#FF4500",
        fallbackBadgeInitial: "Mi");

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MistralProvider> _logger;

    public MistralProvider(HttpClient httpClient, ILogger<MistralProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { CreateUnavailableUsage("API Key missing") };
        }

        // Mistral does not have a public usage/billing API endpoint
        // We verify the key works by calling the models list endpoint
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.mistral.ai/v1/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Mistral AI",
                    IsAvailable = true,
                    RequestsPercentage = 0,
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    Description = "Connected (Check Dashboard)",
                    UsageUnit = "Status",
                    RawJson = content,
                    HttpStatus = (int)response.StatusCode
                }};
            }
            else
            {
                return new[] { CreateUnavailableUsageFromStatus(response) };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Mistral API key");
            return new[] { CreateUnavailableUsageFromException(ex, "Failed to verify Mistral API key") };
        }
    }
}


