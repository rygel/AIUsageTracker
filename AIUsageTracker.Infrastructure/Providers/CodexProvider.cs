// <copyright file="CodexProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageTracker.Core.Helpers;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Constants;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class CodexProvider : ProviderBase
{
    private const string CodexSparkProviderId = "codex.spark";

    private const string WeeklyWindowLabel = "Weekly";

    public static ProviderDefinition StaticDefinition { get; } = new(
        "codex",
        "OpenAI (Codex)",
        PlanType.Coding,
        isQuotaBased: true)
    {
        DiscoveryEnvironmentVariables = new[] { "CODEX_API_KEY" },
        SettingsMode = ProviderSettingsMode.SessionAuthStatus,
        SessionStatusLabel = "OpenAI (Codex)",
        SessionIdentitySource = ProviderSessionIdentitySource.Codex,
        SupportsAccountIdentity = true,
        IconAssetName = "openai",
        BadgeColorHex = "#008B8B",
        BadgeInitial = "AI",
        AuthIdentityCandidatePathTemplates = new[]
        {
            "%USERPROFILE%\\.codex\\auth.json",
            "%APPDATA%\\codex\\auth.json",
        },
        SessionAuthFileSchemas = new[]
        {
            new ProviderAuthFileSchema("tokens", "access_token", "account_id", "id_token"),
        },
        SessionIdentityProfileRootProperties = new[]
        {
            ProviderEndpoints.OpenAI.ProfileClaimKey,
        },
        FamilyMode = ProviderFamilyMode.Standalone,
        CoReportedProviderIds = new[] { CodexSparkProviderId },
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Burst,   "5h",     PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.Rolling, WeeklyWindowLabel, PeriodDuration: TimeSpan.FromDays(7)),
        },
    };

    public static ProviderDefinition SparkDefinition { get; } = new(
        CodexSparkProviderId,
        "OpenAI (GPT-5.3 Codex Spark)",
        PlanType.Coding,
        isQuotaBased: true)
    {
        IconAssetName = "openai",
        BadgeColorHex = "#008B8B",
        BadgeInitial = "AI",
        FamilyMode = ProviderFamilyMode.Standalone,
        ShowInSettings = false,
        SettingsMode = ProviderSettingsMode.SessionAuthStatus,
        SessionStatusLabel = "OpenAI (Codex)",
        AuthIdentityCandidatePathTemplates = new[]
        {
            "%USERPROFILE%\\.codex\\auth.json",
            "%APPDATA%\\codex\\auth.json",
        },
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Burst,   "5h",     PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.Rolling, WeeklyWindowLabel, PeriodDuration: TimeSpan.FromDays(7)),
        },
    };

    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private const string ResetCreditsEndpoint = "https://chatgpt.com/backend-api/wham/rate-limit-reset-credits";
    private const string AuthClaimKey = "https://api.openai.com/auth";
    private const string JsonKeyRateLimit = "rate_limit";
    private const string JsonKeyPrimaryWindow = "primary_window";
    private const string JsonKeySecondaryWindow = "secondary_window";
    private const string JsonKeyUsedPercent = "used_percent";
    private const string JsonKeyResetAfterSeconds = "reset_after_seconds";
    private const string JsonKeyResetAt = "reset_at";
    private const string JsonKeyRateLimitResetCredits = "rate_limit_reset_credits";
    private const string JsonKeyAvailableCount = "available_count";
    private const string JsonKeyLimitWindowSeconds = "limit_window_seconds";
    private const double WeeklyWindowSeconds = 604800.0;

    private readonly HttpClient _httpClient;
    private readonly ILogger<CodexProvider> _logger;
    private readonly string? _authFilePath;

    public CodexProvider(HttpClient httpClient, ILogger<CodexProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public CodexProvider(HttpClient httpClient, ILogger<CodexProvider> logger, string? authFilePath)
    {
        this._httpClient = httpClient;
        this._logger = logger;
        this._authFilePath = string.IsNullOrWhiteSpace(authFilePath) ? null : authFilePath;
    }

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        string? knownAccountIdentity = null;
        try
        {
            var auth = await this.LoadNativeAuthAsync().ConfigureAwait(false);
            var accessToken = auth?.AccessToken;
            var accountId = auth?.AccountId;
            var authIdentity = auth?.Identity;
            knownAccountIdentity = ResolveKnownAccountIdentity(authIdentity, accountId);

            // Allow explicit config/env token as fallback when auth.json is not available.
            if (string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(config.ApiKey))
            {
                accessToken = config.ApiKey;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new[] { this.CreateUnavailableUsageWithIdentity("Codex auth token not found (~/.codex/auth.json or CODEX_API_KEY)", knownAccountIdentity, ProviderUsageState.Missing) };
            }

            var resolvedAccessToken = accessToken!;
            var payload = SessionIdentityHelper.TryDecodeJwtPayload(resolvedAccessToken);
            var email = payload.HasValue
                ? SessionIdentityHelper.TryGetPreferredIdentity(payload.Value, StaticDefinition.SessionIdentityProfileRootProperties)
                : null;
            var jwtPlanType = payload?.ReadString(AuthClaimKey, "chatgpt_plan_type");
            knownAccountIdentity = ResolveKnownAccountIdentity(email, authIdentity, accountId);

            using var request = CreateAuthenticatedRequest(UsageEndpoint, resolvedAccessToken, accountId);
            using var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new[]
                {
                    this.CreateUnavailableUsageWithIdentity(
                        $"HTTP {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)}: {response.ReasonPhrase}",
                        knownAccountIdentity),
                };
            }

            using var jsonDoc = JsonDocument.Parse(content);
            if (TryGetErrorDetailMessage(jsonDoc.RootElement, out var detailMessage))
            {
                return new[] { this.CreateUnavailableUsageWithIdentity(detailMessage, knownAccountIdentity) };
            }

            try
            {
                var httpStatus = (int)response.StatusCode;
                var resetCreditExpirations = await this.GetResetCreditExpirationsAsync(
                    jsonDoc.RootElement,
                    resolvedAccessToken,
                    accountId,
                    cancellationToken).ConfigureAwait(false);
                return this.BuildUsages(
                    config,
                    jsonDoc.RootElement,
                    email,
                    jwtPlanType,
                    authIdentity,
                    accountId,
                    resetCreditExpirations,
                    content,
                    httpStatus);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                this._logger.LogWarning(ex, "Failed to parse Codex usage data. Raw response: {RawResponse}", content.Substring(0, Math.Min(2000, content.Length)));
                return new[] { this.CreateUnavailableUsageWithIdentity("Failed to parse Codex usage data", knownAccountIdentity) };
            }
        }
        catch (JsonException ex)
        {
            this._logger.LogWarning(ex, "Failed to parse Codex native usage response");
            return new[] { this.CreateUnavailableUsageWithIdentity("Invalid Codex usage response format", knownAccountIdentity) };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            this._logger.LogWarning(ex, "Codex native usage lookup failed");
            return new[] { this.CreateUnavailableUsageWithIdentity($"Codex native lookup failed: {ex.Message}", knownAccountIdentity) };
        }
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(string endpoint, string accessToken, string? accountId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", accessToken),
            },
        };

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
        }

        return request;
    }

    private async Task<IReadOnlyList<DateTime>?> GetResetCreditExpirationsAsync(
        JsonElement usageRoot,
        string accessToken,
        string? accountId,
        CancellationToken cancellationToken)
    {
        var availableCount = usageRoot.ReadDouble(JsonKeyRateLimitResetCredits, JsonKeyAvailableCount);
        if (availableCount is not > 0)
        {
            return null;
        }

        try
        {
            using var request = CreateAuthenticatedRequest(ResetCreditsEndpoint, accessToken, accountId);
            using var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogWarning(
                    "Codex reset-credit detail lookup failed with HTTP {StatusCode}",
                    (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(content);
            return ReadResetCreditExpirations(document.RootElement);
        }
        catch (Exception ex) when (
            ex is JsonException or HttpRequestException or IOException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            this._logger.LogWarning(ex, "Codex reset-credit detail lookup failed");
            return null;
        }
    }

    private static bool TryGetErrorDetailMessage(JsonElement root, out string message)
    {
        if (root.TryGetProperty("detail", out var detail) &&
            detail.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(detail.GetString()))
        {
            message = detail.GetString()!;
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static string? ResolveKnownAccountIdentity(params string?[] candidates)
    {
        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
    }

    private static DateTime? ResolveWindowResetTime(double? resetAtEpoch, double? resetAfterSeconds)
    {
        if (resetAtEpoch.HasValue && resetAtEpoch.Value > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)resetAtEpoch.Value).LocalDateTime;
        }

        return ResolveResetTimeFromSeconds(resetAfterSeconds);
    }

    private static string? ResolveAccountIdentity(
        JsonElement root,
        string? jwtEmail,
        string? authIdentity,
        string? accountId)
    {
        var directIdentity = SessionIdentityHelper.TryGetPreferredIdentity(root);
        if (!string.IsNullOrWhiteSpace(directIdentity))
        {
            return directIdentity;
        }

        if (SessionIdentityHelper.IsEmailLike(jwtEmail))
        {
            return jwtEmail;
        }

        if (SessionIdentityHelper.IsEmailLike(authIdentity))
        {
            return authIdentity;
        }

        if (!string.IsNullOrWhiteSpace(authIdentity))
        {
            return authIdentity;
        }

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            return accountId;
        }

        return null;
    }

    private static CodexAuth? TryReadNativeAuth(JsonElement root)
    {
        foreach (var schema in StaticDefinition.SessionAuthFileSchemas)
        {
            if (!root.TryGetProperty(schema.RootProperty, out var authRoot) || authRoot.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var accessToken = authRoot.ReadString(schema.AccessTokenProperty);
            var identityToken = !string.IsNullOrWhiteSpace(schema.IdentityTokenProperty)
                ? authRoot.ReadString(schema.IdentityTokenProperty)
                : null;

            var accountId = !string.IsNullOrWhiteSpace(schema.AccountIdProperty)
                ? authRoot.ReadString(schema.AccountIdProperty)
                : null;

            var identitySource = string.Equals(schema.RootProperty, "tokens", StringComparison.OrdinalIgnoreCase)
                ? root
                : authRoot;
            var identity = ResolveIdentityFromAuthPayload(identitySource, accessToken ?? string.Empty, identityToken);
            if (string.IsNullOrWhiteSpace(accessToken) &&
                string.IsNullOrWhiteSpace(identity) &&
                string.IsNullOrWhiteSpace(accountId))
            {
                continue;
            }

            return new CodexAuth
            {
                AccessToken = accessToken,
                AccountId = accountId,
                Identity = identity,
            };
        }

        return null;
    }

    private static string? ResolveIdentityFromAuthPayload(JsonElement source, string accessToken, string? idToken = null)
    {
        var directIdentity = SessionIdentityHelper.TryGetPreferredIdentity(source, StaticDefinition.SessionIdentityProfileRootProperties);
        if (!string.IsNullOrWhiteSpace(directIdentity))
        {
            return directIdentity;
        }

        if (!string.IsNullOrWhiteSpace(idToken))
        {
            var emailFromIdToken = SessionIdentityHelper.TryGetIdentityFromJwt(idToken, StaticDefinition.SessionIdentityProfileRootProperties);
            if (!string.IsNullOrWhiteSpace(emailFromIdToken))
            {
                return emailFromIdToken;
            }
        }

        var emailFromJwt = SessionIdentityHelper.TryGetIdentityFromJwt(accessToken, StaticDefinition.SessionIdentityProfileRootProperties);
        if (!string.IsNullOrWhiteSpace(emailFromJwt))
        {
            return emailFromJwt;
        }

        return null;
    }

    private ProviderUsage CreateUnavailableUsageWithIdentity(
        string message,
        string? accountIdentity,
        ProviderUsageState state = ProviderUsageState.Error)
    {
        var usage = this.CreateUnavailableUsage(message, state: state);
        usage.AccountName = accountIdentity ?? string.Empty;
        return usage;
    }

    private List<ProviderUsage> BuildUsages(
        ProviderConfig config,
        JsonElement root,
        string? jwtEmail,
        string? jwtPlanType,
        string? authIdentity,
        string? accountId,
        IReadOnlyList<DateTime>? resetCreditExpirations,
        string? rawJson = null,
        int httpStatus = 200)
    {
        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(this.ProviderId);
        var planType = root.ReadString("plan_type") ?? jwtPlanType ?? "unknown";

        // Read limit_window_seconds to determine actual window type
        var primaryLimit = root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyLimitWindowSeconds);
        var hasSecondaryWindow = root.TryGetProperty(JsonKeyRateLimit, out var rateLimitObj) &&
                                 rateLimitObj.TryGetProperty(JsonKeySecondaryWindow, out var secondaryWin) &&
                                 secondaryWin.ValueKind == JsonValueKind.Object;

        var primaryUsedPercent = root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyUsedPercent);
        var primaryResetSeconds = root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyResetAfterSeconds);
        var secondaryUsedPercent = hasSecondaryWindow
            ? root.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyUsedPercent)
            : (double?)null;
        var secondaryResetSeconds = hasSecondaryWindow
            ? root.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyResetAfterSeconds)
            : (double?)null;

        var accountIdentity = ResolveAccountIdentity(root, jwtEmail, authIdentity, accountId);

        var resetCreditsDouble = root.ReadDouble(JsonKeyRateLimitResetCredits, JsonKeyAvailableCount);
        int? resetCreditsAvailable = resetCreditsDouble.HasValue ? (int)resetCreditsDouble.Value : (int?)null;

        var usages = new List<ProviderUsage>();

        // Primary card — always created from primary_window
        {
            var primaryResetTime = ResolveWindowResetTime(
                root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyResetAt),
                primaryResetSeconds);
            if (primaryUsedPercent.HasValue || primaryResetTime.HasValue)
            {
                var pct = primaryUsedPercent ?? 0.0;
                var remaining = Math.Clamp(100.0 - pct, 0.0, 100.0);
                var isWeekly = primaryLimit.HasValue && Math.Abs(primaryLimit.Value - WeeklyWindowSeconds) < 1;

                var primaryCard = CreateModelScopedUsage(config);
                primaryCard.ProviderName = providerLabel;
                primaryCard.CardId = isWeekly ? "weekly" : "burst";
                primaryCard.GroupId = config.ProviderId;
                primaryCard.Name = isWeekly ? WeeklyWindowLabel : "5h";
                primaryCard.WindowKind = isWeekly ? WindowKind.Rolling : WindowKind.Burst;
                primaryCard.UsedPercent = pct;
                primaryCard.RequestsUsed = pct;
                primaryCard.RequestsAvailable = 100.0;
                primaryCard.IsQuotaBased = this.Definition.IsQuotaBased;
                primaryCard.PlanType = this.Definition.PlanType;
                primaryCard.IsAvailable = true;
                primaryCard.Description = $"{remaining.ToString("F0", CultureInfo.InvariantCulture)}% remaining";
                primaryCard.AccountName = accountIdentity ?? string.Empty;
                primaryCard.AuthSource = AuthSource.CodexNative(planType);
                primaryCard.NextResetTime = primaryResetTime;
                primaryCard.PeriodDuration = isWeekly ? TimeSpan.FromDays(7) : TimeSpan.FromHours(5);
                primaryCard.ResetCreditsAvailable = resetCreditsAvailable;
                primaryCard.ResetCreditExpirationsUtc = resetCreditExpirations;
                primaryCard.RawJson = rawJson;
                primaryCard.HttpStatus = httpStatus;
                usages.Add(primaryCard);
            }
        }

        // Secondary card — created from secondary_window if present
        if (secondaryUsedPercent.HasValue)
        {
            var weeklyResetTime = ResolveWindowResetTime(
                root.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyResetAt),
                secondaryResetSeconds);
            var weeklyRemaining = Math.Clamp(100.0 - secondaryUsedPercent.Value, 0.0, 100.0);
            var weeklyCard = CreateModelScopedUsage(config);
            weeklyCard.ProviderName = providerLabel;
            weeklyCard.CardId = "weekly";
            weeklyCard.GroupId = config.ProviderId;
            weeklyCard.Name = WeeklyWindowLabel;
            weeklyCard.WindowKind = WindowKind.Rolling;
            weeklyCard.UsedPercent = secondaryUsedPercent.Value;
            weeklyCard.RequestsUsed = secondaryUsedPercent.Value;
            weeklyCard.RequestsAvailable = 100.0;
            weeklyCard.IsQuotaBased = this.Definition.IsQuotaBased;
            weeklyCard.PlanType = this.Definition.PlanType;
            weeklyCard.IsAvailable = true;
            weeklyCard.Description = $"{weeklyRemaining.ToString("F0", CultureInfo.InvariantCulture)}% remaining";
            weeklyCard.AccountName = accountIdentity ?? string.Empty;
            weeklyCard.AuthSource = AuthSource.CodexNative(planType);
            weeklyCard.NextResetTime = weeklyResetTime;
            weeklyCard.PeriodDuration = TimeSpan.FromDays(7);
            weeklyCard.RawJson = rawJson;
            weeklyCard.HttpStatus = httpStatus;
            usages.Add(weeklyCard);
        }

        if (usages.Count == 0)
        {
            this._logger.LogWarning("[CODEX] No usage window data in response — returning error");
            return new List<ProviderUsage>
            {
                this.CreateUnavailableUsageWithIdentity("No usage window data in response", accountIdentity, httpStatus),
            };
        }

        return usages;
    }

    private async Task<CodexAuth?> LoadNativeAuthAsync()
    {
        foreach (var path in this.GetAuthFileCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var auth = TryReadNativeAuth(doc.RootElement);
                if (auth != null)
                {
                    return auth;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                this._logger.LogDebug(ex, "Failed to read Codex auth file at {Path}", path);
            }
        }

        return null;
    }

    private IEnumerable<string> GetAuthFileCandidates()
    {
        if (!string.IsNullOrWhiteSpace(this._authFilePath))
        {
            yield return this._authFilePath;
            yield break;
        }

        var discoverySpec = StaticDefinition.CreateAuthDiscoverySpec();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var path in ProviderAuthCandidatePathResolver.ResolvePaths(discoverySpec, userProfile))
        {
            yield return path;
        }
    }

    private static IReadOnlyList<DateTime>? ReadResetCreditExpirations(JsonElement root)
    {
        if (!root.TryGetProperty("credits", out var credits) || credits.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var found = new List<DateTime>();
        foreach (var entry in credits.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("expires_at", out var expiresAt) &&
                TryReadIsoTimestamp(expiresAt, out var timestamp))
            {
                found.Add(timestamp);
            }
        }

        if (found.Count == 0)
        {
            return null;
        }

        // Earliest-to-expire (soonest) first.
        found.Sort();
        return found;
    }

    private static bool TryReadIsoTimestamp(JsonElement el, out DateTime timestamp)
    {
        timestamp = default;
        if (el.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = el.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            timestamp = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    private sealed class CodexAuth
    {
        public string? AccessToken { get; set; }

        public string? AccountId { get; set; }

        public string? Identity { get; set; }
    }
}
