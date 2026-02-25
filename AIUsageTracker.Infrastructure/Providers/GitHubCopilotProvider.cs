using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

public class GitHubCopilotProvider : IProviderService
{
    public string ProviderId => "github-copilot";
    private readonly IGitHubAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubCopilotProvider> _logger;

    public GitHubCopilotProvider(HttpClient httpClient, ILogger<GitHubCopilotProvider> logger, IGitHubAuthService authService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _authService = authService;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        var token = ResolveToken(config);
        if (string.IsNullOrEmpty(token))
        {
            return new[] { CreateUnavailableUsage("Not authenticated. Please login in Settings.") };
        }

        var state = new CopilotUsageState
        {
            IsAvailable = true,
            Description = "Authenticated",
            Username = string.Empty,
            PlanName = string.Empty
        };

        try
        {
            using var request = CreateBearerRequest("https://api.github.com/user", token);
            using var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new[] { CreateUnavailableUsage("Authentication failed (401). Please re-login.") };
            }

            if (response.IsSuccessStatusCode)
            {
                await PopulateProfileAndCopilotDataAsync(token, response, state);
            }

            await PopulateUsernameFallbackAsync(state);

            if (!response.IsSuccessStatusCode)
            {
                state.Description = $"Error: {response.StatusCode}";
                state.IsAvailable = false;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching GitHub profile");
            state.Description = "Network Error: Unable to reach GitHub";
            state.IsAvailable = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch GitHub profile");
            state.Description = $"Error: {ex.Message}";
            state.IsAvailable = false;
        }

        return new[] { BuildUsageResult(state) };
    }

    private string? ResolveToken(ProviderConfig config)
    {
        var token = _authService.GetCurrentToken();
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
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("login", out var loginElement))
        {
            state.Username = NormalizeUsername(loginElement.GetString());
        }

        await PopulatePlanNameAsync(token, state);
        await PopulateQuotaSnapshotAsync(token, state);
    }

    private async Task PopulatePlanNameAsync(string token, CopilotUsageState state)
    {
        try
        {
            using var internalRequest = CreateBearerRequest("https://api.github.com/copilot_internal/v2/token", token);
            using var internalResponse = await _httpClient.SendAsync(internalRequest);
            if (internalResponse.IsSuccessStatusCode)
            {
                var internalJson = await internalResponse.Content.ReadAsStringAsync();
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

        var fallbackUsername = NormalizeUsername(await _authService.GetUsernameAsync());
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
            using var quotaResponse = await _httpClient.SendAsync(quotaRequest);
            if (!quotaResponse.IsSuccessStatusCode)
            {
                return;
            }

            var quotaJson = await quotaResponse.Content.ReadAsStringAsync();
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

            if (root.TryGetProperty("quota_snapshots", out var snapshots) &&
                snapshots.TryGetProperty("premium_interactions", out var premium) &&
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
            ProviderId = ProviderId,
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
            NextResetTime = state.ResetTime
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

    private ProviderUsage CreateUnavailableUsage(string description)
    {
        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "GitHub Copilot",
            IsAvailable = false,
            Description = description,
            IsQuotaBased = true,
            PlanType = PlanType.Coding
        };
    }

    private static string NormalizeCopilotPlanName(string plan)
    {
        return plan switch
        {
            "copilot_individual" => "Copilot Individual",
            "copilot_business" => "Copilot Business",
            "copilot_enterprise" => "Copilot Enterprise",
            "copilot_free" => "Copilot Free",
            _ => plan
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
    }
}

