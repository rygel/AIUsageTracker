using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using System.Net;
using System.Net.Http.Headers;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenAIProvider : IProviderService
{
    private const string WhamUsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    public string ProviderId => "openai";
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProvider> _logger;

    public OpenAIProvider(HttpClient httpClient, ILogger<OpenAIProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (!string.IsNullOrWhiteSpace(config.ApiKey) && IsApiKey(config.ApiKey))
        {
            return await GetApiKeyUsageAsync(config.ApiKey);
        }

        var accessToken = config.ApiKey;
        string? accountId = null;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            var nativeAuth = await LoadOpenCodeAuthAsync();
            accessToken = nativeAuth?.Access;
            accountId = nativeAuth?.AccountId;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = false,
                    Description = "OpenAI API key or OpenCode session not found.",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
                }
            };
        }

        try
        {
            return new[] { await GetNativeUsageAsync(accessToken, accountId) };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI session check failed");
            return new[] { CreateUnavailableUsage("Session lookup failed") };
        }
    }

    private async Task<IEnumerable<ProviderUsage>> GetApiKeyUsageAsync(string apiKey)
    {
        if (apiKey.StartsWith("sk-proj", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = false,
                    Description = "Project keys (sk-proj-...) not supported yet. Use a standard user API key.",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
                }
            };
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "OpenAI",
                        IsAvailable = true,
                        RequestsPercentage = 0,
                        IsQuotaBased = false,
                        PlanType = PlanType.Usage,
                        Description = "Connected (API Key)",
                        UsageUnit = "Status"
                    }
                };
            }

            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = false,
                    Description = $"Invalid Key ({response.StatusCode})",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI API key validation failed");
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = false,
                    Description = "Connection Failed",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
                }
            };
        }
    }

    private async Task<ProviderUsage> GetNativeUsageAsync(string accessToken, string? accountId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WhamUsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
        }

        using var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return CreateUnavailableUsage($"Session invalid ({(int)response.StatusCode})");
        }

        if (!response.IsSuccessStatusCode)
        {
            return CreateUnavailableUsage($"Session usage request failed ({(int)response.StatusCode})");
        }

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
        {
            return CreateUnavailableUsage(detail.GetString() ?? "Session usage request failed");
        }

        var planType = ReadString(doc.RootElement, "plan_type") ?? "chatgpt";
        var used = ReadDouble(doc.RootElement, "rate_limit", "primary_window", "used_percent") ?? 0.0;
        var nextResetTime = ResolveResetTime(doc.RootElement);
        var remaining = Math.Clamp(100.0 - used, 0.0, 100.0);

        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "OpenAI (Codex)",
            AccountName = GetAccountIdentity(doc.RootElement, accessToken, accountId) ?? string.Empty,
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            RequestsPercentage = remaining,
            RequestsUsed = used,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            Description = $"{remaining:F0}% remaining ({used:F0}% used) | Plan: {planType}",
            AuthSource = "OpenCode Session",
            NextResetTime = nextResetTime,
            Details = BuildOpenAiSessionDetails(doc.RootElement)
        };
    }

    private ProviderUsage CreateUnavailableUsage(string description)
    {
        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "OpenAI",
            IsAvailable = false,
            Description = description,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsageUnit = "Quota %"
        };
    }

    private static bool IsApiKey(string token)
    {
        return token.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<OpenCodeOpenAiAuth?> LoadOpenCodeAuthAsync()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "auth.json")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("openai", out var openai) || openai.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var access = ReadString(openai, "access");
                if (string.IsNullOrWhiteSpace(access))
                {
                    continue;
                }

                return new OpenCodeOpenAiAuth
                {
                    Access = access,
                    AccountId = ReadString(openai, "accountId")
                };
            }
            catch
            {
                // continue with next candidate
            }
        }

        return null;
    }

    private static List<ProviderUsageDetail> BuildOpenAiSessionDetails(JsonElement root)
    {
        var details = new List<ProviderUsageDetail>();
        var used = ReadDouble(root, "rate_limit", "primary_window", "used_percent");
        var reset = ReadDouble(root, "rate_limit", "primary_window", "reset_after_seconds");
        var primaryResetTime = ResolveWindowResetTime(root, "primary_window");

        if (used.HasValue)
        {
            details.Add(new ProviderUsageDetail
            {
                Name = "5-hour quota",
                Used = $"{used.Value:F0}% used",
                Description = reset.HasValue && reset.Value > 0 ? $"Resets in {(int)reset.Value}s" : string.Empty,
                NextResetTime = primaryResetTime
            });
        }

        var weeklyUsed = ReadDouble(root, "rate_limit", "secondary_window", "used_percent");
        var weeklyReset = ReadDouble(root, "rate_limit", "secondary_window", "reset_after_seconds");
        var weeklyResetTime = ResolveWindowResetTime(root, "secondary_window");
        if (weeklyUsed.HasValue)
        {
            details.Add(new ProviderUsageDetail
            {
                Name = "Weekly quota",
                Used = $"{weeklyUsed.Value:F0}% used",
                Description = weeklyReset.HasValue && weeklyReset.Value > 0 ? $"Resets in {(int)weeklyReset.Value}s" : string.Empty,
                NextResetTime = weeklyResetTime
            });
        }

        var credits = ReadDouble(root, "credits", "balance");
        var unlimited = ReadBool(root, "credits", "unlimited");
        if (credits.HasValue || unlimited.HasValue)
        {
            details.Add(new ProviderUsageDetail
            {
                Name = "Credits",
                Used = unlimited == true ? "Unlimited" : credits?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"
            });
        }

        return details;
    }

    private static DateTime? ResolveResetTime(JsonElement root)
    {
        var primaryReset = ResolveWindowResetTime(root, "primary_window");
        if (primaryReset.HasValue)
        {
            return primaryReset;
        }

        return ResolveWindowResetTime(root, "secondary_window");
    }

    private static DateTime? ResolveWindowResetTime(JsonElement root, string windowName)
    {
        var resetSeconds = ReadDouble(root, "rate_limit", windowName, "reset_after_seconds")
                          ?? ReadDouble(root, "rate_limit", windowName, "reset_after");

        if (resetSeconds.HasValue && resetSeconds.Value > 0)
        {
            return DateTime.UtcNow.AddSeconds(resetSeconds.Value).ToLocalTime();
        }

        var resetAtIso = ReadString(root, "rate_limit", windowName, "resets_at")
                         ?? ReadString(root, "rate_limit", windowName, "reset_at");

        if (!string.IsNullOrWhiteSpace(resetAtIso) &&
            DateTime.TryParse(resetAtIso, out var parsedResetAt))
        {
            return parsedResetAt.ToLocalTime();
        }

        var resetAtEpoch = ReadDouble(root, "rate_limit", windowName, "reset_at_unix");
        if (resetAtEpoch.HasValue && resetAtEpoch.Value > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)resetAtEpoch.Value).LocalDateTime;
        }

        return null;
    }

    private static string? GetAccountIdentity(JsonElement root, string accessToken, string? accountId)
    {
        foreach (var key in new[] { "email", "upn" })
        {
            if (root.TryGetProperty(key, out var claimElement) && claimElement.ValueKind == JsonValueKind.String)
            {
                var value = claimElement.GetString();
                if (IsEmailLike(value))
                {
                    return value;
                }
            }
        }

        var profileEmail = ReadString(root, "https://api.openai.com/profile", "email");
        if (IsEmailLike(profileEmail))
        {
            return profileEmail;
        }

        var claims = DecodeJwtClaims(accessToken);
        if (!string.IsNullOrWhiteSpace(claims.Email))
        {
            return claims.Email;
        }

        foreach (var key in new[] { "preferred_username", "username", "login", "name" })
        {
            if (root.TryGetProperty(key, out var claimElement) && claimElement.ValueKind == JsonValueKind.String)
            {
                var value = claimElement.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            return accountId;
        }

        return null;
    }

    private static (string? Email, string? PlanType) DecodeJwtClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return (null, null);
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);

            string? email = null;
            foreach (var claim in new[] { "email", "upn" })
            {
                if (doc.RootElement.TryGetProperty(claim, out var claimElement) && claimElement.ValueKind == JsonValueKind.String)
                {
                    var value = claimElement.GetString();
                    if (IsEmailLike(value))
                    {
                        email = value;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(email) &&
                doc.RootElement.TryGetProperty("https://api.openai.com/profile", out var profile) &&
                profile.ValueKind == JsonValueKind.Object)
            {
                foreach (var claim in new[] { "email", "username", "name" })
                {
                    var profileValue = ReadString(profile, claim);
                    if (IsEmailLike(profileValue))
                    {
                        email = profileValue;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                foreach (var claim in new[] { "preferred_username", "username", "login", "name", "sub" })
                {
                    if (doc.RootElement.TryGetProperty(claim, out var claimElement) && claimElement.ValueKind == JsonValueKind.String)
                    {
                        var value = claimElement.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            email = value;
                            break;
                        }
                    }
                }
            }

            var planType = ReadString(doc.RootElement, "https://api.openai.com/auth", "plan_type")
                           ?? ReadString(doc.RootElement, "plan_type");

            return (email, planType);
        }
        catch
        {
            return (null, null);
        }
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

        if (current.ValueKind == JsonValueKind.String)
        {
            var raw = current.GetString();
            if (!string.IsNullOrWhiteSpace(raw) &&
                double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
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

    private static bool IsEmailLike(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains('@');
    }

    private sealed class OpenCodeOpenAiAuth
    {
        public string? Access { get; set; }
        public string? AccountId { get; set; }
    }
}

