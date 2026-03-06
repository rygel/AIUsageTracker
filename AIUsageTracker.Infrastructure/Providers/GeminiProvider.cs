using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;

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
        rooConfigPropertyNames: new[] { "geminiApiKey" },
        iconAssetName: "google",
        fallbackBadgeColorHex: "#1E90FF",
        fallbackBadgeInitial: "G");

    public override ProviderDefinition Definition => StaticDefinition;
    public override string ProviderId => StaticDefinition.ProviderId;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiProvider> _logger;
    private readonly string? _accountsPathOverride;
    private readonly string? _oauthCredsPathOverride;

    // Public OAuth client ID embedded in the open-source gemini-cli tool.
    // This is NOT a secret — it is intentionally public and shipped with the CLI.
    private const string GeminiCliClientId =
        "10710060605" + "91-tmhssin2h21lcre235vtoloj" + "h4g403ep.apps.googleusercontent.com";

    // Alternative client ID from the VS Code / JetBrains plugin which sometimes has better access.
    private const string GeminiPluginClientId =
        "681255809395" + "-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";

    public GeminiProvider(HttpClient httpClient, ILogger<GeminiProvider> logger)
        : this(httpClient, logger, null, null)
    {
    }

    // Constructor for testing
    internal GeminiProvider(HttpClient httpClient, ILogger<GeminiProvider> logger, string? accountsPathOverride, string? oauthCredsPathOverride)
    {
        _httpClient = httpClient;
        _logger = logger;
        _accountsPathOverride = accountsPathOverride;
        _oauthCredsPathOverride = oauthCredsPathOverride;
    }

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // 1. Load Accounts
        var accounts = LoadAntigravityAccounts();
        if (accounts == null || accounts.Accounts == null || !accounts.Accounts.Any())
        {
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Gemini CLI",
                IsAvailable = false,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Description = "No Gemini accounts found"
            }};
        }

        var results = new List<ProviderUsage>();

        foreach (var account in accounts.Accounts)
        {
            try
            {
                var accessToken = await RefreshToken(account.RefreshToken);
                var buckets = await FetchQuota(accessToken, account.ProjectId);
                var allBuckets = buckets ?? new List<Bucket>();

                double minFrac = 1.0;
                string mainResetStr = "";
                DateTime? soonestResetDt = null;
                var details = new List<ProviderUsageDetail>();

                if (allBuckets.Any())
                {
                    foreach (var bucket in allBuckets)
                    {
                        minFrac = Math.Min(minFrac, bucket.RemainingFraction);
                        string name = "Quota Bucket";
                        if (bucket.ExtensionData != null && bucket.ExtensionData.TryGetValue("quotaId", out var qidElement))
                        {
                           var qid = qidElement;
                           name = qid.ValueKind != JsonValueKind.Null ? qid.ToString() : "Unknown";
                           name = System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
                           name = name.Replace("Requests Per Day", "(Day)").Replace("Requests Per Minute", "(Min)");
                        }

                        var bucketRemainingPercentage = UsageMath.ClampPercent(bucket.RemainingFraction * 100.0);
                        string? resetTime = bucket.ResetTime;

                        if (string.IsNullOrEmpty(resetTime) && bucket.ExtensionData != null && bucket.ExtensionData.TryGetValue("quotaId", out qidElement))
                        {
                            var qid = qidElement.ToString();
                            if (qid.Contains("RequestsPerDay", StringComparison.OrdinalIgnoreCase))
                                resetTime = DateTime.UtcNow.Date.AddDays(1).ToString("o");
                            else if (qid.Contains("RequestsPerMinute", StringComparison.OrdinalIgnoreCase))
                                resetTime = DateTime.UtcNow.AddMinutes(1).ToString("o");
                        }

                        string resetStr = "";
                        DateTime? itemResetDt = null;
                        if (!string.IsNullOrEmpty(resetTime))
                        {
                            if (DateTime.TryParse(resetTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                            {
                                 var diff = dt.ToLocalTime() - DateTime.Now;
                                 if (diff.TotalSeconds > 0)
                                 {
                                     resetStr = $" (Resets: ({dt.ToLocalTime():MMM dd HH:mm}))";
                                     itemResetDt = dt.ToLocalTime();
                                     bucket.ResetTime = resetTime;
                                 }
                            }
                        }

                        details.Add(new ProviderUsageDetail
                        {
                            Name = name,
                            Used = $"{bucketRemainingPercentage:F1}%",
                            Description = $"{bucket.RemainingFraction:P1} remaining{resetStr}",
                            NextResetTime = itemResetDt,
                            DetailType = ProviderUsageDetailType.QuotaWindow,
                            WindowKind = WindowKind.Primary
                        });
                    }
                }

                // Sort details
                details = details.OrderBy(d => d.Name).ToList();

                var remainingPercentage = UsageMath.ClampPercent(minFrac * 100.0);
                var usedPercentage = 100.0 - remainingPercentage;

                var soonestBucket = allBuckets.Where(b => !string.IsNullOrEmpty(b.ResetTime))
                                             .OrderBy(b => DateTime.TryParse(b.ResetTime, out var dt) ? dt : DateTime.MaxValue)
                                             .FirstOrDefault();

                if (soonestBucket != null && DateTime.TryParse(soonestBucket.ResetTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var sdt))
                {
                    var diff = sdt.ToLocalTime() - DateTime.Now;
                    if (diff.TotalSeconds > 0)
                    {
                         mainResetStr = $" (Resets: ({sdt.ToLocalTime():MMM dd HH:mm}))";
                         soonestResetDt = sdt.ToLocalTime();
                    }
                }

                results.Add(new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Gemini CLI",
                    RequestsPercentage = remainingPercentage,
                    RequestsUsed = usedPercentage,
                    RequestsAvailable = 100,
                    UsageUnit = "Quota %",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    AccountName = account.Email, // Separate usage per account
                    Description = $"{remainingPercentage:F1}% Remaining{mainResetStr}",
                    NextResetTime = soonestResetDt,
                    Details = details,
                    RawJson = JsonSerializer.Serialize(new { buckets = allBuckets }),
                    HttpStatus = 200
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to fetch Gemini quota for {account.Email}");
                results.Add(new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Gemini CLI",
                    IsAvailable = false,
                    Description = $"Error: {ex.Message}",
                    AccountName = account.Email
                });
            }
        }

        if (results.Any(r => r.IsAvailable))
        {
            results = results.Where(r => r.IsAvailable).ToList();
        }

        if (!results.Any())
        {
             return new[] { new ProviderUsage
             {
                 ProviderId = ProviderId,
                 ProviderName = "Gemini CLI",
                 IsAvailable = false,
                 Description = "Failed to fetch quota for any account"
             }};
        }

        return results;
    }

    private AntigravityAccounts? LoadAntigravityAccounts()
    {
        var path = _accountsPathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "antigravity-accounts.json");
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AntigravityAccounts>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load antigravity-accounts.json");
            return null;
        }
    }

    private async Task<string> RefreshToken(string refreshToken)
    {
        string clientId = GeminiCliClientId;

        // Logic to prefer Plugin Client ID if specified in oauth_creds.json (used in tests)
        if (!string.IsNullOrEmpty(_oauthCredsPathOverride) && File.Exists(_oauthCredsPathOverride))
        {
            try
            {
                var json = File.ReadAllText(_oauthCredsPathOverride);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("id_token", out var idToken))
                {
                    var token = idToken.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        var parts = token.Split('.');
                        if (parts.Length > 1)
                        {
                            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/').PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=')));
                            using var payloadDoc = JsonDocument.Parse(payload);
                            if (payloadDoc.RootElement.TryGetProperty("aud", out var aud) && aud.GetString() == GeminiPluginClientId)
                            {
                                clientId = GeminiPluginClientId;
                            }
                        }
                    }
                }
            }
            catch { /* Ignore */ }
        }

        try
        {
            return await DoRefreshToken(refreshToken, clientId);
        }
        catch when (clientId == GeminiCliClientId)
        {
            // If default client fails, retry with plugin client
            return await DoRefreshToken(refreshToken, GeminiPluginClientId);
        }
    }

    private async Task<string> DoRefreshToken(string refreshToken, string clientId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", "" },
            { "refresh_token", refreshToken },
            { "grant_type", "refresh_token" }
        });
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<GeminiTokenResponse>();
        return tokenResponse?.AccessToken ?? throw new Exception("Failed to retrieve access token");
    }

    private async Task<List<Bucket>?> FetchQuota(string accessToken, string projectId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { project = projectId });

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<GeminiQuotaResponse>();
        return data?.Buckets;
    }

    private class AntigravityAccounts
    {
        public List<Account>? Accounts { get; set; }
    }

    private class Account
    {
        public string Email { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string ProjectId { get; set; } = "";
    }

    private class GeminiTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
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

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
