// <copyright file="CodexProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
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
    public static ProviderDefinition StaticDefinition { get; } = new(
        "codex",
        "OpenAI (Codex)",
        PlanType.Coding,
        isQuotaBased: true)
    {
        DisplayNameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["codex.spark"] = "OpenAI (GPT-5.3 Codex Spark)",
        },
        FamilyMode = ProviderFamilyMode.FlatWindowCards,
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
        SettingsAdditionalProviderIds = new[] { "codex.spark" },
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Burst,         "5h",     ChildProviderId: "codex.burst",   PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.Rolling,       "Weekly", ChildProviderId: "codex.weekly",  PeriodDuration: TimeSpan.FromDays(7)),
            new(WindowKind.ModelSpecific, "Spark",  ChildProviderId: "codex.spark",   PeriodDuration: TimeSpan.FromDays(7)),
        },
    };

    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private const string AuthClaimKey = "https://api.openai.com/auth";

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
            using var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new[]
                {
                    this.CreateUnavailableUsageWithIdentity(
                        $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
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
                return this.BuildUsages(jsonDoc.RootElement, email, jwtPlanType, authIdentity, accountId, content, httpStatus);
            }
            catch (Exception ex)
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
        catch (Exception ex)
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
        return candidates.Where(c => !string.IsNullOrWhiteSpace(c)).FirstOrDefault();
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

    private static string BuildUsageDescription(
        double remainingPercent,
        double primaryUsedPercent,
        double? sparkUsedPercent,
        string planType)
    {
        var description = $"{remainingPercent:F0}% remaining ({primaryUsedPercent:F0}% used) | Plan: {planType}";
        if (sparkUsedPercent.HasValue)
        {
            description += $" | Spark: {sparkUsedPercent.Value:F0}% used";
        }

        return description;
    }

    private static string? NormalizeModelName(string? raw, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var normalized = raw.Trim();
        normalized = normalized.Replace('_', '-');
        normalized = normalized.Replace("  ", " ");
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static DateTime? ResolveNextResetTime(double? primaryResetSeconds, double? sparkResetSeconds)
    {
        var resetSeconds = primaryResetSeconds ?? sparkResetSeconds;
        if (!resetSeconds.HasValue || resetSeconds.Value <= 0)
        {
            return null;
        }

        return DateTime.UtcNow.AddSeconds(resetSeconds.Value).ToLocalTime();
    }

    private static double ResolveEffectiveUsedPercent(
        double primaryUsedPercent,
        double? secondaryUsedPercent,
        double? sparkPrimaryUsedPercent,
        double? sparkSecondaryUsedPercent)
    {
        // Return the highest usage percentage across all windows.
        // This ensures the parent entry shows meaningful data even if the API
        // returns 0 for rate_limit.primary_window but has usage in other windows.
        var candidates = new[]
        {
            primaryUsedPercent,
            secondaryUsedPercent ?? 0.0,
            sparkPrimaryUsedPercent ?? 0.0,
            sparkSecondaryUsedPercent ?? 0.0,
        };

        return candidates.Max();
    }

    private static SparkWindow ExtractSparkWindow(JsonElement root)
    {
        var candidates = new List<SparkWindow>();

        // Look in additional_rate_limits array - these are spark windows by structure
        if (root.TryGetProperty("additional_rate_limits", out var additionalRateLimits) &&
            additionalRateLimits.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in additionalRateLimits.EnumerateArray())
            {
                // Spark windows have a model_name or model field and rate_limit data
                var modelName = item.ReadString("model_name") ?? item.ReadString("model");

                if (!item.TryGetProperty("rate_limit", out var sparkRateLimit))
                {
                    continue;
                }

                var primaryUsedPercent = sparkRateLimit.ReadDouble("primary_window", "used_percent");
                var primaryResetAfterSeconds = sparkRateLimit.ReadDouble("primary_window", "reset_after_seconds");
                var secondaryUsedPercent = sparkRateLimit.ReadDouble("secondary_window", "used_percent");
                var secondaryResetAfterSeconds = sparkRateLimit.ReadDouble("secondary_window", "reset_after_seconds");
                if (primaryUsedPercent.HasValue || primaryResetAfterSeconds.HasValue || secondaryUsedPercent.HasValue || secondaryResetAfterSeconds.HasValue)
                {
                    var limitName = item.ReadString("limit_name");
                    candidates.Add(new SparkWindow(
                        limitName,
                        modelName,
                        primaryUsedPercent,
                        primaryResetAfterSeconds,
                        secondaryUsedPercent,
                        secondaryResetAfterSeconds));
                }
            }
        }

        var preferredAdditionalCandidate = SelectPreferredSparkCandidate(candidates);
        if (preferredAdditionalCandidate.HasValue)
        {
            return preferredAdditionalCandidate.Value;
        }

        // Look in rate_limit object properties
        if (root.TryGetProperty("rate_limit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
        {
            candidates.Clear();
            foreach (var property in rateLimit.EnumerateObject())
            {
                if (property.Name.Equals("primary_window", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("secondary_window", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip scalar properties (e.g. "allowed": true, "limit_reached": false) —
                // the API added these alongside the window objects and they are not spark windows.
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var primaryUsedPercent = property.Value.ReadDouble("primary_window", "used_percent");
                var primaryResetAfterSeconds = property.Value.ReadDouble("primary_window", "reset_after_seconds");
                var secondaryUsedPercent = property.Value.ReadDouble("secondary_window", "used_percent");
                var secondaryResetAfterSeconds = property.Value.ReadDouble("secondary_window", "reset_after_seconds");
                if (primaryUsedPercent.HasValue || primaryResetAfterSeconds.HasValue || secondaryUsedPercent.HasValue || secondaryResetAfterSeconds.HasValue)
                {
                    var modelName = property.Value.ReadString("model_name") ?? property.Value.ReadString("model");
                    candidates.Add(new SparkWindow(
                        property.Name,
                        modelName,
                        primaryUsedPercent,
                        primaryResetAfterSeconds,
                        secondaryUsedPercent,
                        secondaryResetAfterSeconds));
                }
            }
        }

        var preferredRateLimitCandidate = SelectPreferredSparkCandidate(candidates);
        return preferredRateLimitCandidate ?? new SparkWindow(null, null, null, null, null, null);
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
        JsonElement root,
        string? jwtEmail,
        string? jwtPlanType,
        string? authIdentity,
        string? accountId,
        string? rawJson = null,
        int httpStatus = 200)
    {
        var planType = root.ReadString("plan_type") ?? jwtPlanType ?? "unknown";
        var primaryUsedPercent = root.ReadDouble("rate_limit", "primary_window", "used_percent") ?? 0.0;
        var primaryResetSeconds = root.ReadDouble("rate_limit", "primary_window", "reset_after_seconds");
        var secondaryUsedPercent = root.ReadDouble("rate_limit", "secondary_window", "used_percent");
        var secondaryResetSeconds = root.ReadDouble("rate_limit", "secondary_window", "reset_after_seconds");
        var sparkWindow = ExtractSparkWindow(root);
        var accountIdentity = ResolveAccountIdentity(root, jwtEmail, authIdentity, accountId);

        var sparkEffectiveWeeklyForParent = sparkWindow.SecondaryUsedPercent ?? secondaryUsedPercent ?? 0.0;
        var effectiveSparkPercent = sparkWindow.HasWindowData
            ? (double?)Math.Max(sparkWindow.PrimaryUsedPercent ?? 0.0, sparkEffectiveWeeklyForParent)
            : null;

        // Primary card: 5-hour burst
        var burstResetTime = ResolveNextResetTime(primaryResetSeconds, null);
        var burstCard = new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = StaticDefinition.DisplayName,
            CardId = "burst",
            GroupId = this.ProviderId,
            Name = "5-hour quota",
            WindowKind = WindowKind.Burst,
            UsedPercent = primaryUsedPercent,
            RequestsUsed = primaryUsedPercent,
            RequestsAvailable = 100.0,
            IsQuotaBased = this.Definition.IsQuotaBased,
            PlanType = this.Definition.PlanType,
            IsAvailable = true,
            Description = $"{Math.Clamp(100.0 - primaryUsedPercent, 0.0, 100.0):F0}% remaining | Plan: {planType}",
            AccountName = accountIdentity ?? string.Empty,
            AuthSource = AuthSource.CodexNative(planType),
            NextResetTime = burstResetTime,
            PeriodDuration = TimeSpan.FromHours(5),
            RawJson = rawJson,
            HttpStatus = httpStatus,
        };

        var usages = new List<ProviderUsage> { burstCard };

        // Weekly card when secondary window data is present
        if (secondaryUsedPercent.HasValue)
        {
            var weeklyResetTime = ResolveResetTimeFromSeconds(secondaryResetSeconds);
            var weeklyRemaining = Math.Clamp(100.0 - secondaryUsedPercent.Value, 0.0, 100.0);
            var weeklyDesc = sparkWindow.HasWindowData && effectiveSparkPercent.HasValue
                ? $"{weeklyRemaining:F0}% remaining | Plan: {planType} | Spark: {effectiveSparkPercent.Value:F0}% used"
                : $"{weeklyRemaining:F0}% remaining | Plan: {planType}";
            usages.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = StaticDefinition.DisplayName,
                CardId = "weekly",
                GroupId = this.ProviderId,
                Name = "Weekly quota",
                WindowKind = WindowKind.Rolling,
                UsedPercent = secondaryUsedPercent.Value,
                RequestsUsed = secondaryUsedPercent.Value,
                RequestsAvailable = 100.0,
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                Description = weeklyDesc,
                AccountName = accountIdentity ?? string.Empty,
                AuthSource = AuthSource.CodexNative(planType),
                NextResetTime = weeklyResetTime,
                PeriodDuration = TimeSpan.FromDays(7),
                RawJson = rawJson,
                HttpStatus = httpStatus,
            });
        }

        // Spark card when Spark window data is present
        if (sparkWindow.HasWindowData)
        {
            var sparkOwnUsed = sparkWindow.PrimaryUsedPercent ?? 0.0;
            var sparkEffectiveUsed = effectiveSparkPercent ?? sparkOwnUsed;
            var sparkBurstResetTime = ResolveNextResetTime(sparkWindow.PrimaryResetAfterSeconds, null);
            var sparkWeeklyResetTime = ResolveResetTimeFromSeconds(sparkWindow.SecondaryResetAfterSeconds ?? secondaryResetSeconds);
            var sparkResetTime = sparkWeeklyResetTime ?? sparkBurstResetTime;
            usages.Add(new ProviderUsage
            {
                ProviderId = "codex.spark",
                ProviderName = StaticDefinition.DisplayNameOverrides.GetValueOrDefault("codex.spark", "OpenAI (GPT-5.3 Codex Spark)"),
                CardId = "spark",
                GroupId = this.ProviderId,
                Name = "Spark",
                UsedPercent = sparkEffectiveUsed,
                RequestsUsed = sparkEffectiveUsed,
                RequestsAvailable = 100.0,
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                Description = $"{Math.Clamp(100.0 - sparkEffectiveUsed, 0.0, 100.0):F0}% remaining | Plan: {planType}",
                AccountName = accountIdentity ?? string.Empty,
                AuthSource = AuthSource.CodexNative(planType),
                NextResetTime = sparkResetTime,
                PeriodDuration = TimeSpan.FromDays(7),
                RawJson = rawJson,
                HttpStatus = httpStatus,
            });
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
            catch (Exception ex)
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

    private static string ResolveModelName(JsonElement root)
    {
        var primaryRaw = root.ReadString("model_name")
                         ?? root.ReadString("model")
                         ?? root.ReadString("rate_limit", "primary_window", "model_name")
                         ?? root.ReadString("rate_limit", "primary_window", "model")
                         ?? root.ReadString("rate_limit", "primary_window", "limit_name");
        return NormalizeModelName(primaryRaw, "OpenAI") ?? "OpenAI";
    }

    private readonly record struct SparkWindow(
        string? Label,
        string? ModelName,
        double? PrimaryUsedPercent,
        double? PrimaryResetAfterSeconds,
        double? SecondaryUsedPercent,
        double? SecondaryResetAfterSeconds)
    {
        /// <summary>
        /// Gets a value indicating whether true when any meaningful window data is present — usage percentages or reset timers.
        /// Allows the Spark block to be processed even when the burst window just reset and the
        /// API omits used_percent (returning only reset_after_seconds).
        /// </summary>
        public bool HasWindowData => this.PrimaryUsedPercent.HasValue || this.SecondaryUsedPercent.HasValue
            || this.PrimaryResetAfterSeconds.HasValue || this.SecondaryResetAfterSeconds.HasValue;
    }

    private sealed class CodexAuth
    {
        public string? AccessToken { get; set; }

        public string? AccountId { get; set; }

        public string? Identity { get; set; }
    }
}
