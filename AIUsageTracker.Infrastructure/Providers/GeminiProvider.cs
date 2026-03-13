// <copyright file="GeminiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class GeminiProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "gemini-cli",
        displayName: "Google Gemini",
        planType: PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based",
        autoIncludeWhenUnconfigured: true,
        includeInWellKnownProviders: true,
        handledProviderIds: new[] { "gemini-cli", "gemini" },
        displayNameOverrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-cli.minute"] = "Gemini CLI (Minute)",
            ["gemini-cli.hourly"] = "Gemini CLI (Hourly)",
            ["gemini-cli.daily"] = "Gemini CLI (Daily)",
        },
        familyMode: ProviderFamilyMode.VisibleDerivedProviders,
        visibleDerivedProviderIds: new[] { "gemini-cli.minute", "gemini-cli.hourly", "gemini-cli.daily" },
        settingsAdditionalProviderIds: new[] { "gemini-cli.minute", "gemini-cli.hourly", "gemini-cli.daily" },
        discoveryEnvironmentVariables: new[] { "GEMINI_API_KEY", "GOOGLE_API_KEY" },
        rooConfigPropertyNames: new[] { "geminiApiKey" },
        supportsAccountIdentity: true,
        derivedModelSelectors: new[]
        {
            new ProviderDerivedModelSelector(
                derivedProviderId: "gemini-cli.minute",
                modelIdContains: new[] { "minute" }),
            new ProviderDerivedModelSelector(
                derivedProviderId: "gemini-cli.hourly",
                modelIdContains: new[] { "hour", "hourly" }),
            new ProviderDerivedModelSelector(
                derivedProviderId: "gemini-cli.daily",
                modelIdContains: new[] { "day", "daily" }),
        },
        iconAssetName: "google",
        fallbackBadgeColorHex: "#1E90FF",
        fallbackBadgeInitial: "G",
        derivedModelDisplaySuffix: "[Gemini CLI]");

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiProvider> _logger;
    private readonly string? _accountsPathOverride;
    private readonly string? _oauthCredsPathOverride;
    private readonly string? _geminiConfigDirectoryOverride;
    private readonly string? _currentDirectoryOverride;

    // Public OAuth client ID embedded in the open-source gemini-cli tool.
    // This is NOT a secret — it is intentionally public and shipped with the CLI.
    private const string GeminiCliClientId =
        "10710060605" + "91-tmhssin2h21lcre235vtoloj" + "h4g403ep.apps.googleusercontent.com";

    private const string GeminiCliClientSecret = "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";

    // Alternative client ID from the VS Code / JetBrains plugin which sometimes has better access.
    private const string GeminiPluginClientId =
        "681255809395" + "-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";

    private const string GeminiPluginClientSecret = "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";

    public GeminiProvider(HttpClient httpClient, ILogger<GeminiProvider> logger)
        : this(httpClient, logger, null, null, null, null)
    {
    }

    // Constructor for testing
    internal GeminiProvider(HttpClient httpClient, ILogger<GeminiProvider> logger, string? accountsPathOverride, string? oauthCredsPathOverride)
        : this(httpClient, logger, accountsPathOverride, oauthCredsPathOverride, null, null)
    {
    }

    // Constructor for testing
    internal GeminiProvider(
        HttpClient httpClient,
        ILogger<GeminiProvider> logger,
        string? accountsPathOverride,
        string? oauthCredsPathOverride,
        string? geminiConfigDirectoryOverride,
        string? currentDirectoryOverride)
    {
        this._httpClient = httpClient;
        this._logger = logger;
        this._accountsPathOverride = accountsPathOverride;
        this._oauthCredsPathOverride = oauthCredsPathOverride;
        this._geminiConfigDirectoryOverride = geminiConfigDirectoryOverride;
        this._currentDirectoryOverride = currentDirectoryOverride;
    }

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // 1. Load Accounts
        var accounts = this.LoadAccounts();
        if (accounts == null || accounts.Accounts == null || !accounts.Accounts.Any())
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    IsAvailable = false,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    Description = "No Gemini accounts found",
                },
            };
        }

        var results = new List<ProviderUsage>();

        foreach (var account in accounts.Accounts)
        {
            try
            {
                this._logger.LogDebug(
                    "Gemini quota refresh started for account {AccountEmail} using project {ProjectId}",
                    account.Email,
                    account.ProjectId);
                var accessToken = await this.RefreshTokenAsync(account.RefreshToken).ConfigureAwait(false);
                var buckets = await this.FetchQuotaAsync(accessToken, account.ProjectId).ConfigureAwait(false);
                var allBuckets = buckets ?? new List<Bucket>();
                var modelQuotaDetails = BuildModelQuotaDetails(allBuckets);
                this._logger.LogDebug(
                    "Gemini quota received {BucketCount} bucket(s) and resolved {ModelCount} model detail(s) for {AccountEmail}: {BucketSummary}",
                    allBuckets.Count,
                    modelQuotaDetails.Count,
                    account.Email,
                    string.Join(
                        ", ",
                        allBuckets.Select(bucket =>
                        {
                            var modelId = TryGetModelId(bucket) ?? "unknown-model";
                            var remaining = UsageMath.ClampPercent(bucket.RemainingFraction * 100.0);
                            var reset = bucket.ResetTime ?? "none";
                            return $"{modelId}:{remaining:F1}%@{reset}";
                        })));

                var minFrac = allBuckets.Count > 0
                    ? allBuckets.Min(bucket => bucket.RemainingFraction)
                    : 1.0;
                string mainResetStr = string.Empty;
                DateTime? soonestResetDt = null;
                var sortedModelQuotaDetails = modelQuotaDetails
                    .OrderBy(d => d.NextResetTime ?? DateTime.MaxValue)
                    .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var remainingPercentage = UsageMath.ClampPercent(minFrac * 100.0);
                var usedPercentage = 100.0 - remainingPercentage;

                var soonestBucket = allBuckets
                    .Select(bucket => ParseResetTimeLocal(bucket.ResetTime))
                    .Where(reset => reset.HasValue)
                    .Select(reset => reset!.Value)
                    .OrderBy(reset => reset)
                    .FirstOrDefault();

                if (soonestBucket != default)
                {
                    var diff = soonestBucket - DateTime.Now;
                    if (diff.TotalSeconds > 0)
                    {
                        mainResetStr = $" (Resets: ({soonestBucket:MMM dd HH:mm}))";
                        soonestResetDt = soonestBucket;
                    }
                }

                var summaryUsage = new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    RequestsPercentage = remainingPercentage,
                    RequestsUsed = usedPercentage,
                    RequestsAvailable = 100,
                    UsageUnit = "Quota %",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    AccountName = account.Email, // Separate usage per account
                    Description = $"{remainingPercentage:F1}% Remaining{mainResetStr}",
                    NextResetTime = soonestResetDt,
                    Details = sortedModelQuotaDetails.Count > 0 ? sortedModelQuotaDetails : null,
                    RawJson = JsonSerializer.Serialize(new { buckets = allBuckets }),
                    HttpStatus = 200,
                };
                results.Add(summaryUsage);
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, $"Failed to fetch Gemini quota for {account.Email}");
                results.Add(new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    IsAvailable = false,
                    Description = $"Error: {ex.Message}",
                    AccountName = account.Email,
                });
            }
        }

        if (results.Any(r => r.IsAvailable))
        {
            results = results.Where(r => r.IsAvailable).ToList();
        }

        if (!results.Any())
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    IsAvailable = false,
                    Description = "Failed to fetch quota for any account",
                },
            };
        }

        return results;
    }

    private AntigravityAccounts? LoadAccounts()
    {
        var opencodeAccounts = this.LoadAntigravityAccounts();
        if (opencodeAccounts?.Accounts?.Any() == true)
        {
            return opencodeAccounts;
        }

        return this.LoadGeminiCliAccounts();
    }

    private AntigravityAccounts? LoadAntigravityAccounts()
    {
        var path = this._accountsPathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "antigravity-accounts.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AntigravityAccounts>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to load antigravity-accounts.json");
            return null;
        }
    }

    private AntigravityAccounts? LoadGeminiCliAccounts()
    {
        var oauthPath = this.ResolveOauthCredsPath();
        if (!File.Exists(oauthPath))
        {
            this._logger.LogDebug("Gemini oauth creds file not found at {Path}", oauthPath);
            return null;
        }

        try
        {
            var oauthJson = File.ReadAllText(oauthPath);
            var oauthCreds = JsonSerializer.Deserialize<GeminiOauthCreds>(
                oauthJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (oauthCreds == null || string.IsNullOrWhiteSpace(oauthCreds.RefreshToken))
            {
                this._logger.LogWarning("Gemini oauth creds did not include refresh_token");
                return null;
            }

            var email = this.ExtractEmailFromIdToken(oauthCreds.IdToken) ?? this.LoadActiveGoogleAccountEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                email = "Gemini Account";
            }

            var projectId = this.ResolveGeminiProjectId();
            if (string.IsNullOrWhiteSpace(projectId))
            {
                this._logger.LogWarning("Gemini projects.json did not provide a usable project ID");
                return null;
            }

            this._logger.LogDebug(
                "Gemini CLI auth resolved account {AccountEmail} with project {ProjectId}",
                email,
                projectId);

            return new AntigravityAccounts
            {
                Accounts = new List<Account>
                {
                    new()
                    {
                        Email = email,
                        RefreshToken = oauthCreds.RefreshToken,
                        ProjectId = projectId,
                    },
                },
            };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to load Gemini CLI auth files");
            return null;
        }
    }

    private string ResolveOauthCredsPath()
    {
        if (!string.IsNullOrWhiteSpace(this._oauthCredsPathOverride))
        {
            return this._oauthCredsPathOverride;
        }

        return Path.Combine(this.ResolveGeminiConfigDirectory(), "oauth_creds.json");
    }

    private string ResolveGeminiConfigDirectory()
    {
        if (!string.IsNullOrWhiteSpace(this._geminiConfigDirectoryOverride))
        {
            return this._geminiConfigDirectoryOverride;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini");
    }

    private string? ResolveGeminiProjectId()
    {
        var projectsPath = Path.Combine(this.ResolveGeminiConfigDirectory(), "projects.json");
        if (!File.Exists(projectsPath))
        {
            this._logger.LogDebug("Gemini projects.json not found at {Path}", projectsPath);
            return null;
        }

        try
        {
            var json = File.ReadAllText(projectsPath);
            var projects = JsonSerializer.Deserialize<GeminiProjects>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (projects?.Projects == null || projects.Projects.Count == 0)
            {
                return null;
            }

            var currentDirectory = this._currentDirectoryOverride ?? Directory.GetCurrentDirectory();
            var normalizedCurrentDirectory = this.NormalizePath(currentDirectory);
            var bestMatch = projects.Projects
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => new
                {
                    Key = this.NormalizePath(pair.Key),
                    Value = pair.Value,
                })
                .Where(pair => normalizedCurrentDirectory.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(pair => pair.Key.Length)
                .FirstOrDefault();
            if (bestMatch != null)
            {
                this._logger.LogDebug(
                    "Gemini project selected by working-directory match. Cwd={CurrentDirectory}; MatchRoot={MatchRoot}; Project={ProjectId}",
                    normalizedCurrentDirectory,
                    bestMatch.Key,
                    bestMatch.Value);
                return bestMatch.Value;
            }

            var fallbackProjectId = projects.Projects.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(fallbackProjectId))
            {
                this._logger.LogDebug(
                    "Gemini project selected by fallback-first-entry. Cwd={CurrentDirectory}; Project={ProjectId}",
                    normalizedCurrentDirectory,
                    fallbackProjectId);
            }

            return fallbackProjectId;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to load Gemini projects.json");
            return null;
        }
    }

    private string? LoadActiveGoogleAccountEmail()
    {
        var accountsPath = Path.Combine(this.ResolveGeminiConfigDirectory(), "google_accounts.json");
        if (!File.Exists(accountsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(accountsPath);
            var accounts = JsonSerializer.Deserialize<GeminiGoogleAccounts>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return accounts?.Active;
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to parse google_accounts.json");
            return null;
        }
    }

    private string? ExtractEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = this.DecodeBase64Url(parts[1]);
            using var payloadDoc = JsonDocument.Parse(payload);
            if (payloadDoc.RootElement.TryGetProperty("email", out var emailElement))
            {
                return emailElement.GetString();
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to extract email from Gemini id_token");
        }

        return null;
    }

    private string NormalizePath(string path)
    {
        var normalized = path.Replace('/', '\\').Trim();
        return normalized.TrimEnd('\\');
    }

    private string DecodeBase64Url(string base64UrlValue)
    {
        var normalized = base64UrlValue.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (normalized.Length % 4)) % 4;
        normalized = normalized.PadRight(normalized.Length + padding, '=');
        var bytes = Convert.FromBase64String(normalized);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<string> RefreshTokenAsync(string refreshToken)
    {
        string clientId = GeminiCliClientId;

        // Logic to prefer Plugin Client ID if specified in oauth_creds.json.
        var oauthCredsPath = this.ResolveOauthCredsPath();
        if (File.Exists(oauthCredsPath))
        {
            try
            {
                var json = File.ReadAllText(oauthCredsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("id_token", out var idToken))
                {
                    var token = idToken.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        var parts = token.Split('.');
                        if (parts.Length > 1)
                        {
                            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/').PadRight(parts[1].Length + ((4 - (parts[1].Length % 4)) % 4), '=')));
                            using var payloadDoc = JsonDocument.Parse(payload);
                            if (payloadDoc.RootElement.TryGetProperty("aud", out var aud) && string.Equals(aud.GetString(), GeminiPluginClientId, StringComparison.Ordinal))
                            {
                                clientId = GeminiPluginClientId;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this._logger.LogDebug(ex, "Failed to inspect Gemini oauth creds for client-id preference");
            }
        }

        try
        {
            var clientSecret = string.Equals(clientId, GeminiPluginClientId, StringComparison.Ordinal)
                ? GeminiPluginClientSecret
                : GeminiCliClientSecret;
            this._logger.LogDebug(
                "Gemini token refresh using OAuth client {ClientKind}",
                string.Equals(clientId, GeminiPluginClientId, StringComparison.Ordinal) ? "plugin" : "cli");
            return await this.DoRefreshTokenAsync(refreshToken, clientId, clientSecret).ConfigureAwait(false);
        }
        catch when (string.Equals(clientId, GeminiCliClientId, StringComparison.Ordinal))
        {
            // If default client fails, retry with plugin client
            this._logger.LogWarning(
                "Gemini token refresh failed with CLI client; retrying with plugin client");
            return await this.DoRefreshTokenAsync(refreshToken, GeminiPluginClientId, GeminiPluginClientSecret).ConfigureAwait(false);
        }
    }

    private async Task<string> DoRefreshTokenAsync(string refreshToken, string clientId, string clientSecret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "refresh_token", refreshToken },
            { "grant_type", "refresh_token" },
        });
        request.Content = content;

        var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<GeminiTokenResponse>().ConfigureAwait(false);
        return tokenResponse?.AccessToken ?? throw new Exception("Failed to retrieve access token");
    }

    private async Task<List<Bucket>?> FetchQuotaAsync(string accessToken, string projectId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { project = projectId });

        var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            this._logger.LogWarning(
                "Gemini quota request failed with status {StatusCode} for project {ProjectId}. Body={ResponseBody}",
                (int)response.StatusCode,
                projectId,
                TruncateForLog(body));
        }

        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<GeminiQuotaResponse>().ConfigureAwait(false);
        return data?.Buckets;
    }

    private static string TruncateForLog(string? value, int maxLength = 600)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    private static IReadOnlyList<ProviderUsageDetail> BuildModelQuotaDetails(IEnumerable<Bucket> buckets)
    {
        var modelBuckets = buckets
            .Select(bucket => new
            {
                Bucket = bucket,
                ModelId = TryGetModelId(bucket),
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ModelId))
            .ToList();
        if (modelBuckets.Count == 0)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        var details = new List<ProviderUsageDetail>();
        foreach (var modelGroup in modelBuckets.GroupBy(entry => entry.ModelId!, StringComparer.OrdinalIgnoreCase))
        {
            var representative = modelGroup
                .OrderBy(entry => entry.Bucket.RemainingFraction)
                .ThenBy(entry => ParseResetTimeLocal(entry.Bucket.ResetTime) ?? DateTime.MaxValue)
                .Select(entry => entry.Bucket)
                .FirstOrDefault();
            if (representative == null)
            {
                continue;
            }

            var remainingPercent = UsageMath.ClampPercent(representative.RemainingFraction * 100.0);
            var resetTime = ParseResetTimeLocal(representative.ResetTime);
            var resetSuffix = resetTime.HasValue ? $" (Resets: ({resetTime.Value:MMM dd HH:mm}))" : string.Empty;

            details.Add(new ProviderUsageDetail
            {
                Name = FormatGeminiModelDisplayName(modelGroup.Key),
                ModelName = modelGroup.Key,
                Description = $"{remainingPercent:F1}% remaining{resetSuffix}",
                NextResetTime = resetTime,
                DetailType = ProviderUsageDetailType.Model,
                QuotaBucketKind = WindowKind.None,
                PercentageValue = remainingPercent,
                PercentageSemantic = PercentageValueSemantic.Remaining,
                PercentageDecimalPlaces = 1,
            });
        }

        return details
            .OrderBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryGetModelId(Bucket bucket)
    {
        if (!string.IsNullOrWhiteSpace(bucket.ModelId))
        {
            return bucket.ModelId;
        }

        if (bucket.ExtensionData == null || !bucket.ExtensionData.TryGetValue("modelId", out var modelIdElement))
        {
            return null;
        }

        var modelId = modelIdElement.ValueKind == JsonValueKind.String
            ? modelIdElement.GetString()
            : modelIdElement.ToString();
        return string.IsNullOrWhiteSpace(modelId) ? null : modelId;
    }

    private static DateTime? ParseResetTimeLocal(string? resetTime)
    {
        if (string.IsNullOrWhiteSpace(resetTime))
        {
            return null;
        }

        if (!DateTime.TryParse(resetTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return null;
        }

        var local = parsed.ToLocalTime();
        return local > DateTime.Now ? local : null;
    }

    private static string FormatGeminiModelDisplayName(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return "Gemini Model";
        }

        var normalized = modelId.Trim();
        if (normalized.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "gemini " + normalized["gemini-".Length..];
        }

        normalized = normalized.Replace("-", " ", StringComparison.Ordinal);

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeModelToken)
            .ToList();

        return tokens.Count == 0 ? modelId : string.Join(' ', tokens);
    }

    private static string NormalizeModelToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        if (token.Any(char.IsDigit))
        {
            return token.ToLowerInvariant();
        }

        return token.ToLowerInvariant() switch
        {
            "gemini" => "Gemini",
            "pro" => "Pro",
            "flash" => "Flash",
            "lite" => "Lite",
            "preview" => "Preview",
            "exp" => "Exp",
            _ => char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant(),
        };
    }

    private class AntigravityAccounts
    {
        public List<Account>? Accounts { get; set; }
    }

    private class Account
    {
        public string Email { get; set; } = string.Empty;

        public string RefreshToken { get; set; } = string.Empty;

        public string ProjectId { get; set; } = string.Empty;
    }

    private class GeminiTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private class GeminiOauthCreds
    {
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }

    private class GeminiGoogleAccounts
    {
        [JsonPropertyName("active")]
        public string? Active { get; set; }
    }

    private class GeminiProjects
    {
        [JsonPropertyName("projects")]
        public Dictionary<string, string>? Projects { get; set; }
    }

    private class GeminiQuotaResponse
    {
        [JsonPropertyName("buckets")]
        public List<Bucket>? Buckets { get; set; }
    }

    private class Bucket
    {
        [JsonPropertyName("remainingFraction")]
        public double RemainingFraction { get; set; }

        [JsonPropertyName("resetTime")]
        public string? ResetTime { get; set; }

        [JsonPropertyName("quotaId")]
        public string? QuotaId { get; set; }

        [JsonPropertyName("modelId")]
        public string? ModelId { get; set; }

        [JsonPropertyName("tokenType")]
        public string? TokenType { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
