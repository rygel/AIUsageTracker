// <copyright file="OpenCodeProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

/// <summary>
/// Queries the OpenCode credits API at <c>https://api.opencode.ai/v1/credits</c>.
/// Shows credit usage as a quota bar (used / total). Complements the CLI-based
/// <see cref="OpenCodeZenProvider"/> which shows cost, sessions, messages, models, and tools.
/// </summary>
public class OpenCodeProvider : ProviderBase
{
    private const string CreditsEndpoint = "https://api.opencode.ai/v1/credits";

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenCodeProvider> _logger;

    public OpenCodeProvider(HttpClient httpClient, ILogger<OpenCodeProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "opencode-go",
        "OpenCode Go",
        PlanType.Usage,
        isQuotaBased: true)
    {
        DiscoveryEnvironmentVariables = new[] { "OPENCODE_API_KEY" },
        SessionAuthFileSchemas = new[]
        {
            new ProviderAuthFileSchema("opencode", "key"),
        },
        AuthIdentityCandidatePathTemplates = new[]
        {
            "%USERPROFILE%\\.local\\share\\opencode\\auth.json",
            "%APPDATA%\\opencode\\auth.json",
            "%LOCALAPPDATA%\\opencode\\auth.json",
            "%USERPROFILE%\\.opencode\\auth.json",
        },
        BadgeColorHex = "#10B981",
        BadgeInitial = "OC",
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = config.ApiKey;
        if (string.IsNullOrEmpty(apiKey) && this.DiscoveryService != null)
        {
            apiKey = this.DiscoveryService.GetEnvironmentVariable("OPENCODE_API_KEY");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return new[]
            {
                this.CreateUnavailableUsage(
                    "API key missing — configure OPENCODE_API_KEY or add key to opencode auth.json",
                    state: ProviderUsageState.Missing),
            };
        }

        try
        {
            var request = CreateBearerRequest(HttpMethod.Get, CreditsEndpoint, apiKey);
            var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var httpStatus = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // The API may return 200 with "Not Found" text when the endpoint isn't available
                this._logger.LogWarning(
                    "OpenCode credits API returned {StatusCode}: {Response}",
                    response.StatusCode,
                    responseBody);
                return new[] { this.CreateUnavailableUsage(DescribeUnavailableStatus(response.StatusCode), httpStatus) };
            }

            // The API returns 200 with text/html "Not Found" for account types that
            // don't support credits. Check Content-Type — a valid response is JSON.
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                this._logger.LogDebug(
                    "OpenCode credits API returned non-JSON content type '{ContentType}' — endpoint not available for this account",
                    contentType);
                return Array.Empty<ProviderUsage>();
            }

            var creditsResponse = DeserializeJsonOrDefault<OpenCodeCreditsResponse>(responseBody);
            if (creditsResponse?.Data == null)
            {
                this._logger.LogWarning("OpenCode credits response missing 'data' field: {Response}", responseBody);
                return new[]
                {
                    this.CreateUnavailableUsage("Invalid response — missing data field", httpStatus),
                };
            }

            var total = creditsResponse.Data.TotalCredits;
            var used = creditsResponse.Data.UsedCredits;
            var remaining = creditsResponse.Data.RemainingCredits;

            // Use remaining_credits from API if available, otherwise compute
            if (remaining <= 0 && total > 0)
            {
                remaining = total - used;
            }

            var usedPercent = total > 0
                ? Math.Clamp(used / total * 100.0, 0, 100)
                : 0;

            this._logger.LogInformation(
                "OpenCode credits: Total={Total}, Used={Used}, Remaining={Remaining}, UsedPercent={UsedPercent}%",
                total, used, remaining, usedPercent);

            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = "OpenCode Go",
                    UsedPercent = usedPercent,
                    RequestsUsed = used,
                    RequestsAvailable = total,
                    PlanType = PlanType.Usage,
                    IsQuotaBased = true,
                    DisplayAsFraction = true,
                    IsAvailable = true,
                    Description = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{remaining:F2} credits remaining ({usedPercent:F0}% used)"),
                    AuthSource = config.AuthSource,
                    RawJson = responseBody,
                    HttpStatus = httpStatus,
                },
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            this._logger.LogWarning(ex, "OpenCode credits API call failed");
            return new[]
            {
                this.CreateUnavailableUsage(DescribeUnavailableException(ex, "Credits API call failed")),
            };
        }
    }

    private sealed class OpenCodeCreditsResponse
    {
        [JsonPropertyName("data")]
        public CreditsData? Data { get; set; }
    }

    private sealed class CreditsData
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("used_credits")]
        public double UsedCredits { get; set; }

        [JsonPropertyName("remaining_credits")]
        public double RemainingCredits { get; set; }
    }
}
