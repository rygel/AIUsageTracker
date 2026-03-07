using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageTracker.Core.Helpers;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class CodexProvider : ProviderBase
{
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private const string ProfileClaimKey = "https://api.openai.com/profile";
    private const string AuthClaimKey = "https://api.openai.com/auth";
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "codex",
        displayName: "OpenAI (Codex)",
        planType: PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based",
        includeInWellKnownProviders: true,
        displayNameOverrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["codex.spark"] = "OpenAI (GPT-5.3-Codex-Spark)"
        },
        supportsChildProviderIds: true,
        discoveryEnvironmentVariables: new[] { "CODEX_API_KEY" },
        nonPersistedProviderIds: new[] { "codex.spark" },
        visibleDerivedProviderIds: new[] { "codex.spark" },
        settingsMode: ProviderSettingsMode.SessionAuthStatus,
        sessionStatusLabel: "OpenAI Codex",
        sessionIdentitySource: ProviderSessionIdentitySource.Codex,
        iconAssetName: "openai",
        fallbackBadgeColorHex: "#008B8B",
        fallbackBadgeInitial: "AI",
        authIdentityCandidatePathTemplates: new[]
        {
            "%USERPROFILE%\\.codex\\auth.json",
            "%APPDATA%\\codex\\auth.json",
            "%USERPROFILE%\\.local\\share\\opencode\\auth.json",
            "%APPDATA%\\opencode\\auth.json",
            "%LOCALAPPDATA%\\opencode\\auth.json",
            "%USERPROFILE%\\.opencode\\auth.json"
        },
        sessionAuthFileSchemas: new[]
        {
            new ProviderAuthFileSchema("tokens", "access_token", "account_id", "id_token"),
            new ProviderAuthFileSchema("openai", "access", "accountId", "id_token")
        });

    public override ProviderDefinition Definition => StaticDefinition;
    public override string ProviderId => StaticDefinition.ProviderId;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CodexProvider> _logger;
    private readonly string? _authFilePath;

    public CodexProvider(HttpClient httpClient, ILogger<CodexProvider> logger)
        : this(httpClient, logger, null)
    {
    }

    public CodexProvider(HttpClient httpClient, ILogger<CodexProvider> logger, string? authFilePath)
    {
        _httpClient = httpClient;
        _logger = logger;
        _authFilePath = string.IsNullOrWhiteSpace(authFilePath) ? null : authFilePath;
    }

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        try
        {
            var auth = await LoadNativeAuthAsync();
            var accessToken = auth?.AccessToken;
            var accountId = auth?.AccountId;
            var authIdentity = auth?.Identity;

            // Allow explicit config/env token as fallback when auth.json is not available.
            if (string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(config.ApiKey))
            {
                accessToken = config.ApiKey;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new[] { CreateUnavailableUsage("Codex auth token not found (~/.codex/auth.json or CODEX_API_KEY)") };
            }

            var resolvedAccessToken = accessToken!;
            var payload = SessionIdentityHelper.TryDecodeJwtPayload(resolvedAccessToken);
            var email = payload.HasValue ? SessionIdentityHelper.TryGetPreferredIdentity(payload.Value) : null;
            var jwtPlanType = payload?.ReadString(AuthClaimKey, "chatgpt_plan_type");

            using var request = CreateUsageRequest(resolvedAccessToken, accountId);
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new[] { CreateUnavailableUsageFromStatus(response) };
            }

            using var jsonDoc = JsonDocument.Parse(content);
            if (TryGetErrorDetailMessage(jsonDoc.RootElement, out var detailMessage))
            {
                return new[] { CreateUnavailableUsage(detailMessage) };
            }

            try
            {
                var httpStatus = (int)response.StatusCode;
                return BuildUsages(jsonDoc.RootElement, email, jwtPlanType, authIdentity, accountId, content, httpStatus);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Codex usage data. Raw response: {RawResponse}", content.Substring(0, Math.Min(2000, content.Length)));
                return new[] { CreateUnavailableUsage("Failed to parse Codex usage data") };
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Codex native usage response");
            return new[] { CreateUnavailableUsage("Invalid Codex usage response format") };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Codex native usage lookup failed");
            return new[] { CreateUnavailableUsage($"Codex native lookup failed: {ex.Message}") };
        }
    }

    private static HttpRequestMessage CreateUsageRequest(string accessToken, string? accountId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", accessToken)
            }
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
        var modelNames = ResolveModelNames(root, sparkWindow);
        var accountIdentity = ResolveAccountIdentity(root, jwtEmail, authIdentity, accountId);

        var remainingPercent = Math.Clamp(100.0 - primaryUsedPercent, 0.0, 100.0);
        var details = BuildDetails(
            primaryUsedPercent,
            primaryResetSeconds,
            secondaryUsedPercent,
            secondaryResetSeconds,
            sparkWindow,
            modelNames,
            root);
        var nextResetTime = ResolveNextResetTime(primaryResetSeconds, sparkWindow.ResetAfterSeconds);
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "OpenAI (Codex)",
                RequestsPercentage = remainingPercent,
                RequestsUsed = 100.0 - remainingPercent,
                RequestsAvailable = 100.0,
                UsageUnit = "Quota %",
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                IsAvailable = true,
                Description = BuildUsageDescription(remainingPercent, primaryUsedPercent, sparkWindow.UsedPercent, planType),
                AccountName = accountIdentity ?? string.Empty,
                AuthSource = $"Codex Native ({planType})",
                NextResetTime = nextResetTime,
                Details = details,
                RawJson = rawJson,
                HttpStatus = httpStatus
            }
        };

        if (sparkWindow.UsedPercent.HasValue)
        {
            usages.Add(CreateSparkUsage(sparkWindow, modelNames, planType, accountIdentity));
        }

        return usages;
    }

    private static ProviderUsage CreateSparkUsage(
        SparkWindow sparkWindow,
        (string PrimaryModelName, string? SparkModelName) modelNames,
        string planType,
        string? accountIdentity)
    {
        var usedPercent = Math.Clamp(sparkWindow.UsedPercent ?? 0.0, 0.0, 100.0);
        var remainingPercent = Math.Clamp(100.0 - usedPercent, 0.0, 100.0);

        return new ProviderUsage
        {
            ProviderId = "codex.spark",
            ProviderName = modelNames.SparkModelName ?? "OpenAI (GPT-5.3-Codex-Spark)",
            RequestsPercentage = remainingPercent,
            RequestsUsed = usedPercent,
            RequestsAvailable = 100.0,
            UsageUnit = "Quota %",
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            IsAvailable = true,
            Description = $"{remainingPercent:F0}% remaining ({usedPercent:F0}% used) | Plan: {planType} (Spark)",
            AccountName = accountIdentity ?? string.Empty,
            AuthSource = $"Codex Native ({planType})",
            NextResetTime = ResolveDetailResetTime(sparkWindow.ResetAfterSeconds),
            Details = new List<ProviderUsageDetail>
            {
                new()
                {
                    Name = modelNames.SparkModelName ?? "OpenAI (GPT-5.3-Codex-Spark)",
                    Used = $"{remainingPercent:F0}% remaining ({usedPercent:F0}% used)",
                    Description = "Model quota",
                    DetailType = ProviderUsageDetailType.Model,
                    WindowKind = WindowKind.Spark
                },
                new()
                {
                    Name = $"Spark ({sparkWindow.Label ?? "window"})",
                    Used = $"{remainingPercent:F0}% remaining ({usedPercent:F0}% used)",
                    Description = FormatResetDescription(sparkWindow.ResetAfterSeconds),
                    NextResetTime = ResolveDetailResetTime(sparkWindow.ResetAfterSeconds),
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    WindowKind = WindowKind.Spark
                }
            }
        };
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

    private async Task<CodexAuth?> LoadNativeAuthAsync()
    {
        foreach (var path in GetAuthFileCandidates())
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
                _logger.LogDebug(ex, "Failed to read Codex auth file at {Path}", path);
            }
        }

        return null;
    }

    private IEnumerable<string> GetAuthFileCandidates()
    {
        if (!string.IsNullOrWhiteSpace(_authFilePath))
        {
            yield return _authFilePath;
            yield break;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        foreach (var pathTemplate in StaticDefinition.AuthIdentityCandidatePathTemplates)
        {
            var path = pathTemplate
                .Replace("%USERPROFILE%", userProfile, StringComparison.OrdinalIgnoreCase)
                .Replace("%APPDATA%", appData, StringComparison.OrdinalIgnoreCase)
                .Replace("%LOCALAPPDATA%", localAppData, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
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
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                continue;
            }

            var identityToken = !string.IsNullOrWhiteSpace(schema.IdentityTokenProperty)
                ? authRoot.ReadString(schema.IdentityTokenProperty)
                : null;

            var accountId = !string.IsNullOrWhiteSpace(schema.AccountIdProperty)
                ? authRoot.ReadString(schema.AccountIdProperty)
                : null;

            var identitySource = string.Equals(schema.RootProperty, "tokens", StringComparison.OrdinalIgnoreCase)
                ? root
                : authRoot;

            return new CodexAuth
            {
                AccessToken = accessToken,
                AccountId = accountId,
                Identity = ResolveIdentityFromAuthPayload(identitySource, accessToken, identityToken)
            };
        }

        return null;
    }

    private static string? ResolveIdentityFromAuthPayload(JsonElement source, string accessToken, string? idToken = null)
    {
        var directIdentity = SessionIdentityHelper.TryGetPreferredIdentity(source);
        if (!string.IsNullOrWhiteSpace(directIdentity))
        {
            return directIdentity;
        }

        if (!string.IsNullOrWhiteSpace(idToken))
        {
            var emailFromIdToken = SessionIdentityHelper.TryGetIdentityFromJwt(idToken);
            if (!string.IsNullOrWhiteSpace(emailFromIdToken))
            {
                return emailFromIdToken;
            }
        }

        var emailFromJwt = SessionIdentityHelper.TryGetIdentityFromJwt(accessToken);
        if (!string.IsNullOrWhiteSpace(emailFromJwt))
        {
            return emailFromJwt;
        }

        return null;
    }

    private static List<ProviderUsageDetail> BuildDetails(
        double primaryUsedPercent,
        double? primaryResetSeconds,
        double? secondaryUsedPercent,
        double? secondaryResetSeconds,
        SparkWindow sparkWindow,
        (string PrimaryModelName, string? SparkModelName) modelNames,
        JsonElement root)
    {
        var primaryRemaining = Math.Clamp(100.0 - primaryUsedPercent, 0.0, 100.0);
        var details = new List<ProviderUsageDetail>
        {
            new()
            {
                Name = modelNames.PrimaryModelName,
                Used = $"{primaryRemaining:F0}% remaining ({primaryUsedPercent:F0}% used)",
                Description = "Model quota",
                DetailType = ProviderUsageDetailType.Model,
                WindowKind = WindowKind.Primary
            },
            new()
            {
                Name = "5-hour quota",
                Used = $"{primaryRemaining:F0}% remaining ({primaryUsedPercent:F0}% used)",
                Description = FormatResetDescription(primaryResetSeconds),
                NextResetTime = ResolveDetailResetTime(primaryResetSeconds),
                DetailType = ProviderUsageDetailType.QuotaWindow,
                WindowKind = WindowKind.Primary
            }
        };

        if (secondaryUsedPercent.HasValue)
        {
            var secondaryRemaining = Math.Clamp(100.0 - secondaryUsedPercent.Value, 0.0, 100.0);
            details.Add(new ProviderUsageDetail
            {
                Name = "Weekly quota",
                Used = $"{secondaryRemaining:F0}% remaining ({secondaryUsedPercent.Value:F0}% used)",
                Description = FormatResetDescription(secondaryResetSeconds),
                NextResetTime = ResolveDetailResetTime(secondaryResetSeconds),
                DetailType = ProviderUsageDetailType.QuotaWindow,
                WindowKind = WindowKind.Secondary
            });
        }

        if (sparkWindow.UsedPercent.HasValue)
        {
            var sparkRemaining = Math.Clamp(100.0 - sparkWindow.UsedPercent.Value, 0.0, 100.0);
            details.Add(new ProviderUsageDetail
            {
                Name = $"Spark ({sparkWindow.Label ?? "window"})",
                Used = $"{sparkRemaining:F0}% remaining ({sparkWindow.UsedPercent.Value:F0}% used)",
                Description = FormatResetDescription(sparkWindow.ResetAfterSeconds),
                NextResetTime = ResolveDetailResetTime(sparkWindow.ResetAfterSeconds),
                DetailType = ProviderUsageDetailType.QuotaWindow,
                WindowKind = WindowKind.Spark
            });
        }

        var creditsBalance = root.ReadDouble("credits", "balance");
        var creditsUnlimited = root.ReadBool("credits", "unlimited");
        if (creditsBalance.HasValue || creditsUnlimited.HasValue)
        {
            var creditValue = creditsUnlimited == true
                ? "Unlimited"
                : creditsBalance?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";

            details.Add(new ProviderUsageDetail
            {
                Name = "Credits",
                Used = creditValue,
                DetailType = ProviderUsageDetailType.Credit,
                WindowKind = WindowKind.None
            });
        }

        return details;
    }

    private static (string PrimaryModelName, string? SparkModelName) ResolveModelNames(JsonElement root, SparkWindow sparkWindow)
    {
        var primaryRaw = root.ReadString("model_name")
                         ?? root.ReadString("model")
                         ?? root.ReadString("rate_limit", "primary_window", "model_name")
                         ?? root.ReadString("rate_limit", "primary_window", "model")
                         ?? root.ReadString("rate_limit", "primary_window", "limit_name");
        var primaryModelName = NormalizeModelName(primaryRaw, "OpenAI (Codex)") ?? "OpenAI (Codex)";

        string? sparkModelName = null;
        if (sparkWindow.UsedPercent.HasValue)
        {
            sparkModelName = ResolveSparkModelName(sparkWindow);
        }

        return (primaryModelName, sparkModelName);
    }

    private static string? ResolveSparkModelName(SparkWindow sparkWindow)
    {
        var explicitModelName = NormalizeModelName(sparkWindow.ModelName, null);
        if (!string.IsNullOrWhiteSpace(explicitModelName))
        {
            return explicitModelName;
        }

        var label = NormalizeModelName(sparkWindow.Label, null);
        if (!string.IsNullOrWhiteSpace(label) && LooksLikeModelName(label))
        {
            return label;
        }

        return null;
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

    private static bool LooksLikeModelName(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var lower = label.ToLowerInvariant();
        return lower.Contains("gpt") ||
               lower.Contains("codex") ||
               lower.Contains("spark") ||
               lower.Contains("claude") ||
               lower.Contains("gemini") ||
               lower.Contains("sonnet");
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

    private static string FormatResetDescription(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return string.Empty;
        }

        return $"Resets in {(int)resetAfterSeconds.Value}s";
    }

    private static DateTime? ResolveDetailResetTime(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return null;
        }

        return DateTime.UtcNow.AddSeconds(resetAfterSeconds.Value).ToLocalTime();
    }

    private static SparkWindow ExtractSparkWindow(JsonElement root)
    {
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

                var usedPercent = sparkRateLimit.ReadDouble("primary_window", "used_percent");
                var resetAfterSeconds = sparkRateLimit.ReadDouble("primary_window", "reset_after_seconds");
                if (usedPercent.HasValue || resetAfterSeconds.HasValue)
                {
                    var limitName = item.ReadString("limit_name");
                    return new SparkWindow(limitName, modelName, usedPercent, resetAfterSeconds);
                }
            }
        }

        // Look in rate_limit object properties
        if (root.TryGetProperty("rate_limit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in rateLimit.EnumerateObject())
            {
                var usedPercent = property.Value.ReadDouble("primary_window", "used_percent");
                var resetAfterSeconds = property.Value.ReadDouble("primary_window", "reset_after_seconds");
                if (usedPercent.HasValue || resetAfterSeconds.HasValue)
                {
                    var modelName = property.Value.ReadString("model_name") ?? property.Value.ReadString("model");
                    return new SparkWindow(property.Name, modelName, usedPercent, resetAfterSeconds);
                }
            }
        }

        return new SparkWindow(null, null, null, null);
    }

    private sealed class CodexAuth
    {
        public string? AccessToken { get; set; }
        public string? AccountId { get; set; }
        public string? Identity { get; set; }
    }

    private readonly record struct SparkWindow(
        string? Label,
        string? ModelName,
        double? UsedPercent,
        double? ResetAfterSeconds);
}


