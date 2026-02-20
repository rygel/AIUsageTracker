using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class CodexProvider : IProviderService
{
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private const string ProfileClaimKey = "https://api.openai.com/profile";
    private const string AuthClaimKey = "https://api.openai.com/auth";

    public string ProviderId => "codex";
    private readonly HttpClient _httpClient;
    private readonly ILogger<CodexProvider> _logger;
    private readonly string _authFilePath;

    public CodexProvider(HttpClient httpClient, ILogger<CodexProvider> logger)
        : this(httpClient, logger, null)
    {
    }

    public CodexProvider(HttpClient httpClient, ILogger<CodexProvider> logger, string? authFilePath)
    {
        _httpClient = httpClient;
        _logger = logger;
        _authFilePath = string.IsNullOrWhiteSpace(authFilePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json")
            : authFilePath;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        try
        {
            var auth = await LoadNativeAuthAsync();
            if (auth == null || string.IsNullOrWhiteSpace(auth.Tokens.AccessToken))
            {
                return new[] { CreateUnavailableUsage("Codex native auth not found (~/.codex/auth.json)") };
            }

            var accessToken = auth.Tokens.AccessToken!;
            var accountId = auth.Tokens.AccountId;
            var (email, jwtPlanType) = DecodeJwtClaims(accessToken);

            using var request = CreateUsageRequest(accessToken, accountId);
            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            var unavailableFromStatus = CreateUnavailableUsageFromStatus(response);
            if (unavailableFromStatus != null)
            {
                return new[] { unavailableFromStatus };
            }

            using var jsonDoc = JsonDocument.Parse(content);
            if (TryGetErrorDetailMessage(jsonDoc.RootElement, out var detailMessage))
            {
                return new[] { CreateUnavailableUsage(detailMessage) };
            }

            return new[] { BuildUsage(jsonDoc.RootElement, email, jwtPlanType) };
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

    private ProviderUsage? CreateUnavailableUsageFromStatus(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return CreateUnavailableUsage($"Authentication failed ({(int)response.StatusCode})");
        }

        if (!response.IsSuccessStatusCode)
        {
            return CreateUnavailableUsage($"Usage request failed ({(int)response.StatusCode})");
        }

        return null;
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

    private ProviderUsage BuildUsage(JsonElement root, string? email, string? jwtPlanType)
    {
        var planType = ReadString(root, "plan_type") ?? jwtPlanType ?? "unknown";
        var primaryUsedPercent = ReadDouble(root, "rate_limit", "primary_window", "used_percent") ?? 0.0;
        var primaryResetSeconds = ReadDouble(root, "rate_limit", "primary_window", "reset_after_seconds");
        var secondaryUsedPercent = ReadDouble(root, "rate_limit", "secondary_window", "used_percent");
        var secondaryResetSeconds = ReadDouble(root, "rate_limit", "secondary_window", "reset_after_seconds");
        var sparkWindow = ExtractSparkWindow(root);

        var remainingPercent = Math.Clamp(100.0 - primaryUsedPercent, 0.0, 100.0);
        var details = BuildDetails(primaryUsedPercent, primaryResetSeconds, secondaryUsedPercent, secondaryResetSeconds, sparkWindow, root);
        var nextResetTime = ResolveNextResetTime(primaryResetSeconds, sparkWindow.ResetAfterSeconds);

        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Codex",
            RequestsPercentage = remainingPercent,
            RequestsUsed = 100.0 - remainingPercent,
            RequestsAvailable = 100.0,
            UsageUnit = "Quota %",
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            IsAvailable = true,
            Description = BuildUsageDescription(remainingPercent, primaryUsedPercent, sparkWindow.UsedPercent, planType),
            AccountName = email ?? string.Empty,
            AuthSource = $"Codex Native ({planType})",
            NextResetTime = nextResetTime,
            Details = details
        };
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
        if (!File.Exists(_authFilePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_authFilePath);
        return JsonSerializer.Deserialize<CodexAuth>(json);
    }

    private static (string? Email, string? PlanType) DecodeJwtClaims(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return (null, null);
        }

        try
        {
            var payloadBytes = DecodeBase64Url(parts[1]);
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;

            string? email = null;
            string? planType = null;

            if (root.TryGetProperty(ProfileClaimKey, out var profile) && profile.ValueKind == JsonValueKind.Object)
            {
                if (profile.TryGetProperty("email", out var emailElement) && emailElement.ValueKind == JsonValueKind.String)
                {
                    email = emailElement.GetString();
                }
            }

            if (root.TryGetProperty(AuthClaimKey, out var auth) && auth.ValueKind == JsonValueKind.Object)
            {
                if (auth.TryGetProperty("chatgpt_plan_type", out var planTypeElement) && planTypeElement.ValueKind == JsonValueKind.String)
                {
                    planType = planTypeElement.GetString();
                }
            }

            return (email, planType);
        }
        catch
        {
            return (null, null);
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;
        if (padding > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(normalized);
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

    private static List<ProviderUsageDetail> BuildDetails(
        double primaryUsedPercent,
        double? primaryResetSeconds,
        double? secondaryUsedPercent,
        double? secondaryResetSeconds,
        (string? Label, double? UsedPercent, double? ResetAfterSeconds) sparkWindow,
        JsonElement root)
    {
        var details = new List<ProviderUsageDetail>
        {
            new()
            {
                Name = "Primary Window",
                Used = $"{primaryUsedPercent:F0}% used",
                Description = FormatResetDescription(primaryResetSeconds)
            }
        };

        if (secondaryUsedPercent.HasValue)
        {
            details.Add(new ProviderUsageDetail
            {
                Name = "Secondary Window",
                Used = $"{secondaryUsedPercent.Value:F0}% used",
                Description = FormatResetDescription(secondaryResetSeconds)
            });
        }

        if (sparkWindow.UsedPercent.HasValue)
        {
            details.Add(new ProviderUsageDetail
            {
                Name = $"Spark ({sparkWindow.Label ?? "window"})",
                Used = $"{sparkWindow.UsedPercent.Value:F0}% used",
                Description = FormatResetDescription(sparkWindow.ResetAfterSeconds)
            });
        }

        var creditsBalance = ReadDouble(root, "credits", "balance");
        var creditsUnlimited = ReadBool(root, "credits", "unlimited");
        if (creditsBalance.HasValue || creditsUnlimited.HasValue)
        {
            var creditValue = creditsUnlimited == true
                ? "Unlimited"
                : creditsBalance?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";

            details.Add(new ProviderUsageDetail
            {
                Name = "Credits",
                Used = creditValue
            });
        }

        return details;
    }

    private static string FormatResetDescription(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return string.Empty;
        }

        return $"Resets in {(int)resetAfterSeconds.Value}s";
    }

    private static (string? Label, double? UsedPercent, double? ResetAfterSeconds) ExtractSparkWindow(JsonElement root)
    {
        if (root.TryGetProperty("rate_limit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in rateLimit.EnumerateObject())
            {
                if (!property.Name.Contains("spark", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var usedPercent = ReadDouble(property.Value, "primary_window", "used_percent");
                var resetAfterSeconds = ReadDouble(property.Value, "primary_window", "reset_after_seconds");
                if (usedPercent.HasValue || resetAfterSeconds.HasValue)
                {
                    return (property.Name, usedPercent, resetAfterSeconds);
                }
            }
        }

        if (root.TryGetProperty("additional_rate_limits", out var additionalRateLimits) &&
            additionalRateLimits.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in additionalRateLimits.EnumerateArray())
            {
                var limitName = ReadString(item, "limit_name");
                if (string.IsNullOrWhiteSpace(limitName) ||
                    !limitName.Contains("spark", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!item.TryGetProperty("rate_limit", out var sparkRateLimit))
                {
                    continue;
                }

                var usedPercent = ReadDouble(sparkRateLimit, "primary_window", "used_percent");
                var resetAfterSeconds = ReadDouble(sparkRateLimit, "primary_window", "reset_after_seconds");
                if (usedPercent.HasValue || resetAfterSeconds.HasValue)
                {
                    return (limitName, usedPercent, resetAfterSeconds);
                }
            }
        }

        return (null, null, null);
    }

    private static string? ReadString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static double? ReadDouble(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var number))
        {
            return number;
        }

        return null;
    }

    private static bool? ReadBool(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private ProviderUsage CreateUnavailableUsage(string message)
    {
        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Codex",
            IsAvailable = false,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            RequestsPercentage = 0,
            RequestsUsed = 0,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            Description = message,
            AuthSource = "Codex Native"
        };
    }

    private sealed class CodexAuth
    {
        [JsonPropertyName("tokens")]
        public CodexTokens Tokens { get; set; } = new();
    }

    private sealed class CodexTokens
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("account_id")]
        public string? AccountId { get; set; }
    }
}
