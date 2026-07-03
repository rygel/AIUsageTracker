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

    /// <summary>
    /// Gets standalone provider definition for the Spark sub-model.
    /// Registered in the catalog so it is grouped independently from the main codex provider,
    /// enabling a separate dual-bar card (5h burst + weekly rolling) in the main window.
    /// </summary>
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
        ShowInSettings = true,
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
    private const string AuthClaimKey = "https://api.openai.com/auth";
    private const string JsonKeyRateLimit = "rate_limit";
    private const string JsonKeyPrimaryWindow = "primary_window";
    private const string JsonKeySecondaryWindow = "secondary_window";
    private const string JsonKeyUsedPercent = "used_percent";
    private const string JsonKeyResetAfterSeconds = "reset_after_seconds";
    private const string JsonKeyResetAt = "reset_at";

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

            using var request = CreateUsageRequest(resolvedAccessToken, accountId);
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
                return this.BuildUsages(config, jsonDoc.RootElement, email, jwtPlanType, authIdentity, accountId, content, httpStatus);
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

    private static HttpRequestMessage CreateUsageRequest(string accessToken, string? accountId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint)
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

    private static SparkWindow ExtractSparkWindow(JsonElement root)
    {
        var candidates = ParseAdditionalRateLimits(root);

        var preferredAdditionalCandidate = SelectPreferredSparkCandidate(candidates);
        if (preferredAdditionalCandidate.HasValue)
        {
            return preferredAdditionalCandidate.Value;
        }

        candidates = ParseRateLimitProperties(root);

        var preferredRateLimitCandidate = SelectPreferredSparkCandidate(candidates);
        return preferredRateLimitCandidate ?? new SparkWindow(Label: null, ModelName: null, PrimaryUsedPercent: null, PrimaryResetTime: null, SecondaryUsedPercent: null, SecondaryResetTime: null);
    }

    private static List<SparkWindow> ParseAdditionalRateLimits(JsonElement root)
    {
        var candidates = new List<SparkWindow>();

        if (!root.TryGetProperty("additional_rate_limits", out var additionalRateLimits) ||
            additionalRateLimits.ValueKind != JsonValueKind.Array)
        {
            return candidates;
        }

        foreach (var item in additionalRateLimits.EnumerateArray())
        {
            var modelName = item.ReadString("model_name") ?? item.ReadString("model");

            if (!item.TryGetProperty(JsonKeyRateLimit, out var sparkRateLimit))
            {
                continue;
            }

            var sparkWindow = TryParseSparkWindowFromElement(sparkRateLimit, item.ReadString("limit_name"), modelName);
            if (sparkWindow.HasValue)
            {
                candidates.Add(sparkWindow.Value);
            }
        }

        return candidates;
    }

    private static List<SparkWindow> ParseRateLimitProperties(JsonElement root)
    {
        var candidates = new List<SparkWindow>();

        if (!root.TryGetProperty(JsonKeyRateLimit, out var rateLimit) || rateLimit.ValueKind != JsonValueKind.Object)
        {
            return candidates;
        }

        foreach (var property in rateLimit.EnumerateObject())
        {
            if (property.Name.Equals(JsonKeyPrimaryWindow, StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals(JsonKeySecondaryWindow, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var modelName = property.Value.ReadString("model_name") ?? property.Value.ReadString("model");
            var sparkWindow = TryParseSparkWindowFromElement(property.Value, property.Name, modelName);
            if (sparkWindow.HasValue)
            {
                candidates.Add(sparkWindow.Value);
            }
        }

        return candidates;
    }

    private static SparkWindow? TryParseSparkWindowFromElement(JsonElement element, string? label, string? modelName)
    {
        var primaryUsedPercent = element.ReadDouble(JsonKeyPrimaryWindow, JsonKeyUsedPercent);
        var primaryResetTime = ResolveWindowResetTime(
            element.ReadDouble(JsonKeyPrimaryWindow, JsonKeyResetAt),
            element.ReadDouble(JsonKeyPrimaryWindow, JsonKeyResetAfterSeconds));
        var secondaryUsedPercent = element.ReadDouble(JsonKeySecondaryWindow, JsonKeyUsedPercent);
        var secondaryResetTime = ResolveWindowResetTime(
            element.ReadDouble(JsonKeySecondaryWindow, JsonKeyResetAt),
            element.ReadDouble(JsonKeySecondaryWindow, JsonKeyResetAfterSeconds));
        if (primaryUsedPercent.HasValue || primaryResetTime.HasValue || secondaryUsedPercent.HasValue || secondaryResetTime.HasValue)
        {
            return new SparkWindow(label, modelName, primaryUsedPercent, primaryResetTime, secondaryUsedPercent, secondaryResetTime);
        }

        return null;
    }

    private static SparkWindow? SelectPreferredSparkCandidate(IReadOnlyCollection<SparkWindow> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var match = candidates.Where(LooksLikeSparkWindow).Cast<SparkWindow?>().FirstOrDefault();
        return match ?? candidates.First();
    }

    private static bool LooksLikeSparkWindow(SparkWindow candidate)
    {
        return ContainsSparkToken(candidate.Label) || ContainsSparkToken(candidate.ModelName);
    }

    private static bool ContainsSparkToken(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains("spark", StringComparison.OrdinalIgnoreCase);
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
        string? rawJson = null,
        int httpStatus = 200)
    {
        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(this.ProviderId);
        var planType = root.ReadString("plan_type") ?? jwtPlanType ?? "unknown";
        var primaryUsedPercent = root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyUsedPercent) ?? 0.0;
        var primaryResetSeconds = root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyResetAfterSeconds);
        var secondaryUsedPercent = root.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyUsedPercent);
        var secondaryResetSeconds = root.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyResetAfterSeconds);
        var sparkWindow = ExtractSparkWindow(root);
        var accountIdentity = ResolveAccountIdentity(root, jwtEmail, authIdentity, accountId);

        var effectiveSparkPercent = sparkWindow.HasWindowData
            ? (double?)Math.Max(sparkWindow.PrimaryUsedPercent ?? 0.0, sparkWindow.SecondaryUsedPercent ?? 0.0)
            : null;

        // Primary card: 5-hour burst
        var burstResetTime = ResolveWindowResetTime(
            root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyResetAt),
            primaryResetSeconds);
        var burstCard = CreateModelScopedUsage(config);
        burstCard.ProviderName = providerLabel;
        burstCard.CardId = "burst";
        burstCard.GroupId = config.ProviderId;
        burstCard.Name = "5h";
        burstCard.WindowKind = WindowKind.Burst;
        burstCard.UsedPercent = primaryUsedPercent;
        burstCard.RequestsUsed = primaryUsedPercent;
        burstCard.RequestsAvailable = 100.0;
        burstCard.IsQuotaBased = this.Definition.IsQuotaBased;
        burstCard.PlanType = this.Definition.PlanType;
        burstCard.IsAvailable = true;
        burstCard.Description = $"{Math.Clamp(100.0 - primaryUsedPercent, 0.0, 100.0).ToString("F0", CultureInfo.InvariantCulture)}% remaining | Plan: {planType}";
        burstCard.AccountName = accountIdentity ?? string.Empty;
        burstCard.AuthSource = AuthSource.CodexNative(planType);
        burstCard.NextResetTime = burstResetTime;
        burstCard.PeriodDuration = TimeSpan.FromHours(5);
        burstCard.RawJson = rawJson;
        burstCard.HttpStatus = httpStatus;

        var usages = new List<ProviderUsage> { burstCard };

        // Weekly card when secondary window data is present
        if (secondaryUsedPercent.HasValue)
        {
            var weeklyResetTime = ResolveWindowResetTime(
                root.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyResetAt),
                secondaryResetSeconds);
            var weeklyRemaining = Math.Clamp(100.0 - secondaryUsedPercent.Value, 0.0, 100.0);
            var weeklyDesc = sparkWindow.HasWindowData && effectiveSparkPercent.HasValue
                ? $"{weeklyRemaining.ToString("F0", CultureInfo.InvariantCulture)}% remaining | Plan: {planType} | Spark: {effectiveSparkPercent.Value.ToString("F0", CultureInfo.InvariantCulture)}% used"
                : $"{weeklyRemaining.ToString("F0", CultureInfo.InvariantCulture)}% remaining | Plan: {planType}";
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
            weeklyCard.Description = weeklyDesc;
            weeklyCard.AccountName = accountIdentity ?? string.Empty;
            weeklyCard.AuthSource = AuthSource.CodexNative(planType);
            weeklyCard.NextResetTime = weeklyResetTime;
            weeklyCard.PeriodDuration = TimeSpan.FromDays(7);
            weeklyCard.RawJson = rawJson;
            weeklyCard.HttpStatus = httpStatus;
            usages.Add(weeklyCard);
        }

        // Spark burst + rolling cards — same pattern as the main Codex cards so
        // the "OpenAI (GPT-5.3 Codex Spark)" card is dual-bar capable.
        if (sparkWindow.HasWindowData)
        {
            var sparkDisplayName = ProviderMetadataCatalog.GetConfiguredDisplayName(CodexSparkProviderId);
            var sparkBurstUsed = sparkWindow.PrimaryUsedPercent ?? 0.0;
            var sparkBurstResetTime = sparkWindow.PrimaryResetTime;

            var sparkBurstCard = CreateModelScopedUsage(config);
            sparkBurstCard.ProviderId = CodexSparkProviderId;
            sparkBurstCard.ProviderName = sparkDisplayName;
            sparkBurstCard.CardId = "spark.burst";
            sparkBurstCard.GroupId = this.ProviderId;
            sparkBurstCard.Name = "5h";
            sparkBurstCard.WindowKind = WindowKind.Burst;
            sparkBurstCard.UsedPercent = sparkBurstUsed;
            sparkBurstCard.RequestsUsed = sparkBurstUsed;
            sparkBurstCard.RequestsAvailable = 100.0;
            sparkBurstCard.IsQuotaBased = this.Definition.IsQuotaBased;
            sparkBurstCard.PlanType = this.Definition.PlanType;
            sparkBurstCard.IsAvailable = true;
            sparkBurstCard.Description = $"{Math.Clamp(100.0 - sparkBurstUsed, 0.0, 100.0).ToString("F0", CultureInfo.InvariantCulture)}% remaining | Plan: {planType}";
            sparkBurstCard.AccountName = accountIdentity ?? string.Empty;
            sparkBurstCard.AuthSource = AuthSource.CodexNative(planType);
            sparkBurstCard.NextResetTime = sparkBurstResetTime;
            sparkBurstCard.PeriodDuration = TimeSpan.FromHours(5);
            sparkBurstCard.RawJson = rawJson;
            sparkBurstCard.HttpStatus = httpStatus;
            usages.Add(sparkBurstCard);

            var sparkWeeklyUsed = sparkWindow.SecondaryUsedPercent ?? 0.0;
            var sparkWeeklyResetTime = sparkWindow.SecondaryResetTime;

            var sparkWeeklyCard = CreateModelScopedUsage(config);
            sparkWeeklyCard.ProviderId = CodexSparkProviderId;
            sparkWeeklyCard.ProviderName = sparkDisplayName;
            sparkWeeklyCard.CardId = "spark.weekly";
            sparkWeeklyCard.GroupId = this.ProviderId;
            sparkWeeklyCard.Name = WeeklyWindowLabel;
            sparkWeeklyCard.WindowKind = WindowKind.Rolling;
            sparkWeeklyCard.UsedPercent = sparkWeeklyUsed;
            sparkWeeklyCard.RequestsUsed = sparkWeeklyUsed;
            sparkWeeklyCard.RequestsAvailable = 100.0;
            sparkWeeklyCard.IsQuotaBased = this.Definition.IsQuotaBased;
            sparkWeeklyCard.PlanType = this.Definition.PlanType;
            sparkWeeklyCard.IsAvailable = true;
            sparkWeeklyCard.Description = $"{Math.Clamp(100.0 - sparkWeeklyUsed, 0.0, 100.0).ToString("F0", CultureInfo.InvariantCulture)}% remaining | Plan: {planType}";
            sparkWeeklyCard.AccountName = accountIdentity ?? string.Empty;
            sparkWeeklyCard.AuthSource = AuthSource.CodexNative(planType);
            sparkWeeklyCard.NextResetTime = sparkWeeklyResetTime;
            sparkWeeklyCard.PeriodDuration = TimeSpan.FromDays(7);
            sparkWeeklyCard.RawJson = rawJson;
            sparkWeeklyCard.HttpStatus = httpStatus;
            usages.Add(sparkWeeklyCard);
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

    private readonly record struct SparkWindow(
        string? Label,
        string? ModelName,
        double? PrimaryUsedPercent,
        DateTime? PrimaryResetTime,
        double? SecondaryUsedPercent,
        DateTime? SecondaryResetTime)
    {
        /// <summary>
        /// Gets a value indicating whether true when any meaningful window data is present — usage percentages or reset timers.
        /// Allows the Spark block to be processed even when the burst window just reset and the
        /// API omits used_percent (returning only reset_after_seconds).
        /// </summary>
        public bool HasWindowData => this.PrimaryUsedPercent.HasValue || this.SecondaryUsedPercent.HasValue
            || this.PrimaryResetTime.HasValue || this.SecondaryResetTime.HasValue;
    }

    private sealed class CodexAuth
    {
        public string? AccessToken { get; set; }

        public string? AccountId { get; set; }

        public string? Identity { get; set; }
    }
}
