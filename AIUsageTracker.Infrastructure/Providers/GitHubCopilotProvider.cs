// <copyright file="GitHubCopilotProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class GitHubCopilotProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "github-copilot",
        displayName: "GitHub Copilot",
        planType: PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based",
        includeInWellKnownProviders: true,
        settingsMode: ProviderSettingsMode.ExternalAuthStatus,
        iconAssetName: "github",
        fallbackBadgeColorHex: "#9370DB",
        fallbackBadgeInitial: "GH",
        authIdentityCandidatePathTemplates: new[]
        {
            "%APPDATA%\\GitHub CLI\\hosts.yml",
            "%USERPROFILE%\\.config\\gh\\hosts.yml",
        });

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    private readonly IGitHubAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubCopilotProvider> _logger;

    public GitHubCopilotProvider(HttpClient httpClient, ILogger<GitHubCopilotProvider> logger, IGitHubAuthService authService)
    {
        this._httpClient = httpClient;
        this._logger = logger;
        this._authService = authService;
    }

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        var token = this.ResolveToken(config);
        if (string.IsNullOrEmpty(token))
        {
            return new[] { this.CreateUnavailableUsage("Not authenticated. Please login in Settings.") };
        }

        var state = new CopilotUsageState
        {
            IsAvailable = true,
            Description = "Authenticated",
            Username = string.Empty,
            PlanName = string.Empty,
        };

        try
        {
            using var request = CreateBearerRequest("https://api.github.com/user", token);
            using var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            state.HttpStatus = (int)response.StatusCode;

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new[] { this.CreateUnavailableUsage("Authentication failed (401). Please re-login.") };
            }

            if (response.IsSuccessStatusCode)
            {
                await this.PopulateProfileAndCopilotDataAsync(token, response, state).ConfigureAwait(false);
            }

            await this.PopulateUsernameFallbackAsync(state).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                state.Description = $"Error: {response.StatusCode}";
                state.IsAvailable = false;
            }
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogError(ex, "Network error fetching GitHub profile");
            state.Description = "Network Error: Unable to reach GitHub";
            state.IsAvailable = false;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to fetch GitHub profile");
            state.Description = $"Error: {ex.Message}";
            state.IsAvailable = false;
        }

        return new[] { this.BuildUsageResult(state) };
    }

    private string? ResolveToken(ProviderConfig config)
    {
        var token = this._authService.GetCurrentToken();
        if (string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(config.ApiKey))
        {
            token = config.ApiKey;
        }

        return token;
    }

    private static HttpRequestMessage CreateBearerRequest(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AIUsageTracker", "1.0"));
        return request;
    }

    private static HttpRequestMessage CreateQuotaRequest(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/user");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {token}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Editor-Version", "vscode/1.96.2");
        request.Headers.TryAddWithoutValidation("X-Github-Api-Version", "2025-04-01");
        request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AIUsageTracker", "1.0"));
        return request;
    }

    private async Task PopulateProfileAndCopilotDataAsync(string token, HttpResponseMessage response, CopilotUsageState state)
    {
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        state.RawJson = json;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("login", out var loginElement))
        {
            state.Username = NormalizeUsername(loginElement.GetString());
        }

        await this.PopulatePlanNameAsync(token, state).ConfigureAwait(false);
        await this.PopulateQuotaSnapshotAsync(token, state).ConfigureAwait(false);
    }

    private async Task PopulatePlanNameAsync(string token, CopilotUsageState state)
    {
        try
        {
            using var internalRequest = CreateBearerRequest("https://api.github.com/copilot_internal/v2/token", token);
            using var internalResponse = await this._httpClient.SendAsync(internalRequest).ConfigureAwait(false);
            if (internalResponse.IsSuccessStatusCode)
            {
                var internalJson = await internalResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var internalDoc = System.Text.Json.JsonDocument.Parse(internalJson);
                var sku = string.Empty;
                if (internalDoc.RootElement.TryGetProperty("sku", out var skuProp))
                {
                    sku = skuProp.GetString() ?? string.Empty;
                }

                state.PlanName = NormalizeCopilotPlanName(sku);
                state.Description = BuildAuthenticatedDescription(state.Username, state.PlanName);
            }
            else
            {
                state.Description = BuildAuthenticatedDescription(state.Username, null);
            }
        }
        catch
        {
            state.Description = BuildAuthenticatedDescription(state.Username, null);
        }
    }

    private async Task PopulateUsernameFallbackAsync(CopilotUsageState state)
    {
        if (HasMeaningfulUsername(state.Username))
        {
            return;
        }

        var fallbackUsername = NormalizeUsername(await this._authService.GetUsernameAsync());
        if (!string.IsNullOrEmpty(fallbackUsername))
        {
            state.Username = fallbackUsername;
        }
    }

    private async Task PopulateQuotaSnapshotAsync(string token, CopilotUsageState state)
    {
        try
        {
            using var quotaRequest = CreateQuotaRequest(token);
            using var quotaResponse = await this._httpClient.SendAsync(quotaRequest).ConfigureAwait(false);
            if (!quotaResponse.IsSuccessStatusCode)
            {
                return;
            }

            var quotaJson = await quotaResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var quotaDoc = System.Text.Json.JsonDocument.Parse(quotaJson);
            var root = quotaDoc.RootElement;

            if (root.TryGetProperty("copilot_plan", out var planProp))
            {
                var quotaPlan = NormalizeCopilotPlanName(planProp.GetString() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(quotaPlan))
                {
                    state.PlanName = quotaPlan;
                }
            }

            if (root.TryGetProperty("quota_reset_date", out var resetProp))
            {
                var resetText = resetProp.GetString();
                if (!string.IsNullOrWhiteSpace(resetText) &&
                    DateTime.TryParse(
                        resetText,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var parsedResetUtc))
                {
                    state.ResetTime = parsedResetUtc.ToLocalTime();
                }
            }

            state.Details = new List<ProviderUsageDetail>();

            if (root.TryGetProperty("quota_snapshots", out var snapshots))
            {
                // 1. Primary Window (Hourly/Short-term)
                if (snapshots.TryGetProperty("usage", out var usageSnapshot) &&
                    usageSnapshot.TryGetProperty("entitlement", out var uEntProp) &&
                    usageSnapshot.TryGetProperty("remaining", out var uRemProp) &&
                    uEntProp.TryGetDouble(out var uEnt) &&
                    uRemProp.TryGetDouble(out var uRem) &&
                    uEnt > 0)
                {
                    var uUsed = Math.Max(0, uEnt - uRem);
                    var uPct = ((uEnt - uRem) / uEnt) * 100.0;
                    state.Details.Add(new ProviderUsageDetail
                    {
                        Name = "5-hour Window",
                        Used = $"{uPct:F0}% used",
                        Description = $"{uRem:F0} / {uEnt:F0} remaining",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        WindowKind = WindowKind.Primary,
                    });
                }

                // 2. Secondary Window (Premium/Interaction-based)
                if (snapshots.TryGetProperty("premium_interactions", out var premium) &&
                    premium.TryGetProperty("entitlement", out var entitlementProp) &&
                    premium.TryGetProperty("remaining", out var remainingProp) &&
                    entitlementProp.TryGetDouble(out var entitlement) &&
                    remainingProp.TryGetDouble(out var remaining) &&
                    entitlement > 0)
                {
                    var normalizedRemaining = Math.Clamp(remaining, 0, entitlement);
                    var used = Math.Max(0, entitlement - normalizedRemaining);
                    state.CostLimit = entitlement;
                    state.CostUsed = used;
                    state.Percentage = UsageMath.CalculateRemainingPercent(used, entitlement);
                    state.HasCopilotQuotaData = true;

                    state.Details.Add(new ProviderUsageDetail
                    {
                        Name = "Weekly Quota",
                        Used = $"{100.0 - state.Percentage:F0}% used",
                        Description = $"{normalizedRemaining:F0} / {entitlement:F0} remaining",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        WindowKind = WindowKind.Secondary,
                        NextResetTime = state.ResetTime,
                    });
                }
            }
        }
        catch
        {
            // Continue with fallback sources.
        }
    }

    private ProviderUsage BuildUsageResult(CopilotUsageState state)
    {
        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = "GitHub Copilot",
            AccountName = HasMeaningfulUsername(state.Username) ? state.Username : string.Empty,
            IsAvailable = state.IsAvailable,
            Description = BuildFinalDescription(state),
            RequestsPercentage = state.Percentage,
            RequestsAvailable = state.CostLimit,
            RequestsUsed = state.CostUsed,
            UsageUnit = "Requests",
            PlanType = PlanType.Coding,
            IsQuotaBased = true,
            AuthSource = string.IsNullOrEmpty(state.PlanName) ? "Unknown" : state.PlanName,
            NextResetTime = state.ResetTime,
            Details = state.Details,
            RawJson = state.RawJson,
            HttpStatus = state.HttpStatus,
        };
    }

    private static string BuildFinalDescription(CopilotUsageState state)
    {
        if (state.HasCopilotQuotaData)
        {
            var description = $"Premium Requests: {state.CostLimit - state.CostUsed:F0}/{state.CostLimit:F0} Remaining";
            if (!string.IsNullOrEmpty(state.PlanName))
            {
                description += $" ({state.PlanName})";
            }

            return description;
        }

        return state.Description;
    }

    private static bool HasMeaningfulUsername(string? username)
    {
        return !string.IsNullOrWhiteSpace(username) &&
               !username.Equals("User", StringComparison.OrdinalIgnoreCase) &&
               !username.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUsername(string? username)
    {
        return HasMeaningfulUsername(username) ? username! : string.Empty;
    }

    private static string BuildAuthenticatedDescription(string username, string? planName)
    {
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(planName))
        {
            return $"Authenticated as {username} ({planName})";
        }

        if (!string.IsNullOrEmpty(username))
        {
            return $"Authenticated as {username}";
        }

        if (!string.IsNullOrEmpty(planName))
        {
            return $"Authenticated ({planName})";
        }

        return "Authenticated";
    }

    private static string NormalizeCopilotPlanName(string plan)
    {
        return plan switch
        {
            "copilot_individual" => "Copilot Individual",
            "copilot_business" => "Copilot Business",
            "copilot_enterprise" => "Copilot Enterprise",
            "copilot_free" => "Copilot Free",
            _ => plan,
        };
    }

    private sealed class CopilotUsageState
    {
        public bool IsAvailable { get; set; }

        public string Description { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string PlanName { get; set; } = string.Empty;

        public DateTime? ResetTime { get; set; }

        public double Percentage { get; set; }

        public double CostUsed { get; set; }

        public double CostLimit { get; set; }

        public bool HasCopilotQuotaData { get; set; }

        public List<ProviderUsageDetail>? Details { get; set; }

        public string? RawJson { get; set; }

        public int HttpStatus { get; set; } = 200;
    }
}
