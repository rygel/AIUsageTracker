// <copyright file="GitHubCopilotProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class GitHubCopilotProvider : ProviderBase
{
    private const string GitHubUserUrl = "https://api.github.com/user";
    private const string CopilotUserUrl = "https://api.github.com/copilot_internal/user";
    private const string CopilotTokenUrl = "https://api.github.com/copilot_internal/v2/token";
    private const string ProviderDisplayName = "GitHub Copilot";

    private readonly IGitHubAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubCopilotProvider> _logger;

    public GitHubCopilotProvider(HttpClient httpClient, ILogger<GitHubCopilotProvider> logger, IGitHubAuthService authService, IProviderDiscoveryService? discoveryService = null)
        : base(discoveryService)
    {
        this._httpClient = httpClient;
        this._logger = logger;
        this._authService = authService;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "github-copilot",
        ProviderDisplayName,
        PlanType.Coding,
        isQuotaBased: true)
    {
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

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

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
                    ProviderName = this.Definition.DisplayName,
                    AccountName = username,
                    IsAvailable = false,
                    State = ProviderUsageState.Missing,
                    Description = "Not authenticated. Please login in Settings.",
                    PlanType = this.Definition.PlanType,
                    IsQuotaBased = this.Definition.IsQuotaBased,
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
            using var request = CreateBearerRequest(GitHubUserUrl, token);
            using var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex) when (ex is TaskCanceledException or System.Text.Json.JsonException)
        {
            this._logger.LogError(ex, "Failed to fetch GitHub profile");
            state.Description = $"Error: {ex.Message}";
            state.IsAvailable = false;
            state.State = ProviderUsageState.Error;
        }

        return this.BuildUsageResults(state);
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
        var request = new HttpRequestMessage(HttpMethod.Get, CopilotUserUrl);
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
            var description = $"{state.PrimaryQuotaWindowName}: {(state.CostLimit - state.CostUsed).ToString("F0", CultureInfo.InvariantCulture)}/{state.CostLimit.ToString("F0", CultureInfo.InvariantCulture)} Remaining";
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
            using var internalRequest = CreateBearerRequest(CopilotTokenUrl, token);
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
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
                    state.WeeklyUsedPercent = usedPercent;
                    state.WeeklyDescription = $"{normalizedRemaining.ToString("F0", CultureInfo.InvariantCulture)} / {entitlement.ToString("F0", CultureInfo.InvariantCulture)} remaining";
                    state.WeeklyEntitlement = entitlement;
                    state.WeeklyUsed = entitlement - normalizedRemaining;
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

                    state.BurstUsedPercent = uUsedPercent;
                    state.BurstDescription = $"{normalizedRemaining.ToString("F0", CultureInfo.InvariantCulture)} / {uEnt.ToString("F0", CultureInfo.InvariantCulture)} remaining";
                    state.BurstEntitlement = uEnt;
                    state.BurstUsed = uEnt - normalizedRemaining;
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            this._logger.LogDebug(ex, "Failed to parse GitHub Copilot quota snapshot");
        }
    }

    private IEnumerable<ProviderUsage> BuildUsageResults(CopilotUsageState state)
    {
        var accountName = HasMeaningfulUsername(state.Username) ? state.Username : string.Empty;
        var authSource = string.IsNullOrEmpty(state.PlanName) ? AuthSource.Unknown : state.PlanName;

        var baseUsage = new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = ProviderDisplayName,
            AccountName = accountName,
            IsAvailable = state.IsAvailable,
            State = state.State,
            Description = BuildFinalDescription(state),
            UsedPercent = 100.0 - state.Percentage,
            RequestsAvailable = state.CostLimit,
            RequestsUsed = state.CostUsed,
            PlanType = this.Definition.PlanType,
            IsQuotaBased = this.Definition.IsQuotaBased,
            AuthSource = authSource,
            NextResetTime = state.ResetTime,
            RawJson = state.RawJson,
            HttpStatus = state.HttpStatus,
        };

        var hasWeekly = state.WeeklyDescription != null;
        var hasBurst = state.BurstDescription != null;

        if (!hasWeekly && !hasBurst)
        {
            return new[] { baseUsage };
        }

        var results = new List<ProviderUsage>();

        if (hasWeekly)
        {
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = ProviderDisplayName,
                CardId = "weekly",
                GroupId = this.ProviderId,
                Name = "Weekly Quota",
                AccountName = accountName,
                IsAvailable = state.IsAvailable,
                State = state.State,
                Description = state.WeeklyDescription!,
                UsedPercent = state.WeeklyUsedPercent,
                RequestsAvailable = state.WeeklyEntitlement,
                RequestsUsed = state.WeeklyUsed,
                PlanType = this.Definition.PlanType,
                IsQuotaBased = this.Definition.IsQuotaBased,
                AuthSource = authSource,
                NextResetTime = state.ResetTime,
                PeriodDuration = TimeSpan.FromDays(7),
                WindowKind = WindowKind.Rolling,
                RawJson = state.RawJson,
                HttpStatus = state.HttpStatus,
            });
        }

        if (hasBurst)
        {
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = ProviderDisplayName,
                CardId = "burst",
                GroupId = this.ProviderId,
                Name = "5-Hour Window",
                AccountName = accountName,
                IsAvailable = state.IsAvailable,
                State = state.State,
                Description = state.BurstDescription!,
                UsedPercent = state.BurstUsedPercent,
                RequestsAvailable = state.BurstEntitlement,
                RequestsUsed = state.BurstUsed,
                PlanType = this.Definition.PlanType,
                IsQuotaBased = this.Definition.IsQuotaBased,
                AuthSource = authSource,
                PeriodDuration = TimeSpan.FromHours(5),
                WindowKind = WindowKind.Burst,
                RawJson = state.RawJson,
                HttpStatus = state.HttpStatus,
            });
        }

        if (results.Count == 0)
        {
            return new[] { baseUsage };
        }

        return results;
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

        // Flat card state (replaces Details list)
        public string? WeeklyDescription { get; set; }

        public double WeeklyUsedPercent { get; set; }

        public double WeeklyEntitlement { get; set; }

        public double WeeklyUsed { get; set; }

        public string? BurstDescription { get; set; }

        public double BurstUsedPercent { get; set; }

        public double BurstEntitlement { get; set; }

        public double BurstUsed { get; set; }

        public string? RawJson { get; set; }

        public int HttpStatus { get; set; } = 200;
    }
}
