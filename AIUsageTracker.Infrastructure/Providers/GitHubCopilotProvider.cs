// <copyright file="GitHubCopilotProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class GitHubCopilotProvider : ProviderBase
{
    private readonly IGitHubAuthService _authService;
    private readonly IResilientHttpClient _resilientHttpClient;
    private readonly ILogger<GitHubCopilotProvider> _logger;

    public GitHubCopilotProvider(IResilientHttpClient resilientHttpClient, ILogger<GitHubCopilotProvider> logger, IGitHubAuthService authService, IProviderDiscoveryService? discoveryService = null)
        : base(discoveryService)
    {
        this._resilientHttpClient = resilientHttpClient;
        this._logger = logger;
        this._authService = authService;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "github-copilot",
        "GitHub Copilot",
        PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based")
    {
        AutoIncludeWhenUnconfigured = true,
        IncludeInWellKnownProviders = true,
        SettingsMode = ProviderSettingsMode.ExternalAuthStatus,
        SupportsAccountIdentity = true,
        IconAssetName = "github",
        BadgeColorHex = "#9370DB",
        BadgeInitial = "GH",
        AuthIdentityCandidatePathTemplates = new[]
        {
            "%APPDATA%\\GitHub CLI\\hosts.yml",
            "%USERPROFILE%\\.config\\gh\\hosts.yml",
        },
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Rolling, "Weekly", PeriodDuration: TimeSpan.FromDays(7)),
            new(WindowKind.Burst,   "5h",     PeriodDuration: TimeSpan.FromHours(5)),
        },
    };

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        var token = this.ResolveToken(config);
        if (string.IsNullOrEmpty(token) && this.DiscoveryService != null)
        {
            var discoveredAuth = await this.DiscoveryService.DiscoverAuthAsync(this.Definition.CreateAuthDiscoverySpec()).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(discoveredAuth?.AccessToken))
            {
                token = discoveredAuth.AccessToken;
                config.ApiKey = token;
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            var username = NormalizeUsername(await this._authService.GetUsernameAsync().ConfigureAwait(false));
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = "GitHub Copilot",
                    AccountName = username,
                    IsAvailable = false,
                    State = ProviderUsageState.Missing,
                    Description = "Not authenticated. Please login in Settings.",
                    PlanType = PlanType.Coding,
                    IsQuotaBased = true,
                },
            };
        }

        // Keep auth-service state in sync with the token source so username resolution
        // works even when token comes from persisted config (not device-flow memory state).
        this._authService.InitializeToken(token);

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
            using var response = await this._resilientHttpClient.SendAsync(request, this.ProviderId).ConfigureAwait(false);
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
                state.State = ProviderUsageState.Error;
            }
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogError(ex, "Network error fetching GitHub profile");
            state.Description = "Network Error: Unable to reach GitHub";
            state.IsAvailable = false;
            state.State = ProviderUsageState.Error;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to fetch GitHub profile");
            state.Description = $"Error: {ex.Message}";
            state.IsAvailable = false;
            state.State = ProviderUsageState.Error;
        }

        return new[] { this.BuildUsageResult(state) };
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

    private static bool TryParseFiniteQuotaSnapshot(
        System.Text.Json.JsonElement snapshot,
        out double entitlement,
        out double remaining,
        out double remainingPercent)
    {
        entitlement = 0;
        remaining = 0;
        remainingPercent = 0;

        if (snapshot.TryGetProperty("unlimited", out var unlimitedProp) &&
            unlimitedProp.ValueKind == System.Text.Json.JsonValueKind.True)
        {
            return false;
        }

        if (!snapshot.TryGetProperty("entitlement", out var entitlementProp) ||
            !entitlementProp.TryGetDouble(out entitlement) ||
            entitlement <= 0)
        {
            return false;
        }

        if (snapshot.TryGetProperty("quota_remaining", out var quotaRemainingProp) &&
            quotaRemainingProp.TryGetDouble(out var quotaRemaining))
        {
            remaining = quotaRemaining;
        }
        else if (snapshot.TryGetProperty("remaining", out var remainingProp) &&
                 remainingProp.TryGetDouble(out var remainingValue))
        {
            remaining = remainingValue;
        }
        else
        {
            remaining = entitlement;
        }

        remaining = Math.Clamp(remaining, 0, entitlement);

        if (snapshot.TryGetProperty("percent_remaining", out var remainingPercentProp) &&
            remainingPercentProp.TryGetDouble(out var parsedRemainingPercent))
        {
            remainingPercent = Math.Clamp(parsedRemainingPercent, 0, 100);
        }
        else
        {
            var used = Math.Max(0, entitlement - remaining);
            remainingPercent = UsageMath.CalculateRemainingPercent(used, entitlement);
        }

        return true;
    }

    private static void ApplyQuotaWindowSnapshot(
        CopilotUsageState state,
        string windowName,
        double entitlement,
        double remaining,
        double remainingPercent)
    {
        if (entitlement <= 0)
        {
            return;
        }

        var normalizedRemaining = Math.Clamp(remaining, 0, entitlement);
        var used = Math.Max(0, entitlement - normalizedRemaining);
        var normalizedRemainingPercent = Math.Clamp(remainingPercent, 0, 100);
        state.HasCopilotQuotaData = true;
        state.CostLimit = entitlement;
        state.CostUsed = used;
        state.Percentage = normalizedRemainingPercent;
        state.PrimaryQuotaWindowName = windowName;
    }

    private static string BuildFinalDescription(CopilotUsageState state)
    {
        if (state.HasCopilotQuotaData)
        {
            var description = $"{state.PrimaryQuotaWindowName}: {state.CostLimit - state.CostUsed:F0}/{state.CostLimit:F0} Remaining";
            if (!string.IsNullOrEmpty(state.PlanName))
            {
                description += $" ({state.PlanName})";
            }

            return description;
        }

        return $"{state.Description} (quota unknown)";
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
            "individual" => "Copilot Individual",
            "business" => "Copilot Business",
            "enterprise" => "Copilot Enterprise",
            "free" => "Copilot Free",
            "copilot_individual" => "Copilot Individual",
            "copilot_business" => "Copilot Business",
            "copilot_enterprise" => "Copilot Enterprise",
            "copilot_free" => "Copilot Free",
            _ => plan,
        };
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
            using var internalResponse = await this._resilientHttpClient.SendAsync(internalRequest, this.ProviderId).ConfigureAwait(false);
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
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to resolve GitHub Copilot plan name");
            state.Description = BuildAuthenticatedDescription(state.Username, null);
        }
    }

    private async Task PopulateUsernameFallbackAsync(CopilotUsageState state)
    {
        if (HasMeaningfulUsername(state.Username))
        {
            return;
        }

        var fallbackUsername = NormalizeUsername(await this._authService.GetUsernameAsync().ConfigureAwait(false));
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
            using var quotaResponse = await this._resilientHttpClient.SendAsync(quotaRequest, this.ProviderId).ConfigureAwait(false);
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
                var selectedWindowName = string.Empty;
                var selectedWindowEntitlement = 0.0;
                var selectedWindowRemaining = 0.0;
                var selectedWindowRemainingPercent = 0.0;

                // 1. Premium/interaction window is the primary top-level signal for Copilot quota.
                if (snapshots.TryGetProperty("premium_interactions", out var premium) &&
                    TryParseFiniteQuotaSnapshot(premium, out var entitlement, out var remaining, out var remainingPercent))
                {
                    var normalizedRemaining = Math.Clamp(remaining, 0, entitlement);
                    var usedPercent = Math.Clamp(100.0 - remainingPercent, 0.0, 100.0);
                    selectedWindowName = "Weekly Quota";
                    selectedWindowEntitlement = entitlement;
                    selectedWindowRemaining = normalizedRemaining;
                    selectedWindowRemainingPercent = remainingPercent;

                    state.Details.Add(new ProviderUsageDetail
                    {
                        Name = "Weekly Quota",
                        Description = $"{normalizedRemaining:F0} / {entitlement:F0} remaining",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Rolling,
                        NextResetTime = state.ResetTime,
                        PercentageValue = usedPercent,
                        PercentageSemantic = PercentageValueSemantic.Used,
                    });
                }

                // 2. Usage/session window is supplementary when present.
                if (snapshots.TryGetProperty("usage", out var usageSnapshot) &&
                    TryParseFiniteQuotaSnapshot(usageSnapshot, out var uEnt, out var uRem, out var uRemainingPercent))
                {
                    var normalizedRemaining = Math.Clamp(uRem, 0, uEnt);
                    var uUsedPercent = Math.Clamp(100.0 - uRemainingPercent, 0.0, 100.0);
                    if (string.IsNullOrEmpty(selectedWindowName))
                    {
                        selectedWindowName = "5-hour Window";
                        selectedWindowEntitlement = uEnt;
                        selectedWindowRemaining = normalizedRemaining;
                        selectedWindowRemainingPercent = uRemainingPercent;
                    }

                    state.Details.Add(new ProviderUsageDetail
                    {
                        Name = "5-hour Window",
                        Description = $"{normalizedRemaining:F0} / {uEnt:F0} remaining",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Burst,
                        PercentageValue = uUsedPercent,
                        PercentageSemantic = PercentageValueSemantic.Used,
                    });
                }

                if (!string.IsNullOrEmpty(selectedWindowName))
                {
                    ApplyQuotaWindowSnapshot(
                        state,
                        selectedWindowName,
                        selectedWindowEntitlement,
                        selectedWindowRemaining,
                        selectedWindowRemainingPercent);
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to parse GitHub Copilot quota snapshot");
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
            State = state.State,
            Description = BuildFinalDescription(state),
            UsedPercent = 100.0 - state.Percentage,
            RequestsAvailable = state.CostLimit,
            RequestsUsed = state.CostUsed,
            PlanType = PlanType.Coding,
            IsQuotaBased = true,
            AuthSource = string.IsNullOrEmpty(state.PlanName) ? AuthSource.Unknown : state.PlanName,
            NextResetTime = state.ResetTime,
            Details = state.Details,
            RawJson = state.RawJson,
            HttpStatus = state.HttpStatus,
        };
    }

    private sealed class CopilotUsageState
    {
        public bool IsAvailable { get; set; }

        public ProviderUsageState State { get; set; } = ProviderUsageState.Available;

        public string Description { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string PlanName { get; set; } = string.Empty;

        public DateTime? ResetTime { get; set; }

        public double Percentage { get; set; }

        public double CostUsed { get; set; }

        public double CostLimit { get; set; }

        public bool HasCopilotQuotaData { get; set; }

        public string PrimaryQuotaWindowName { get; set; } = "Quota";

        public List<ProviderUsageDetail>? Details { get; set; }

        public string? RawJson { get; set; }

        public int HttpStatus { get; set; } = 200;
    }
}
