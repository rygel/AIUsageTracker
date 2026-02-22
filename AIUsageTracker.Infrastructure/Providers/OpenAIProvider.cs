using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        if (string.IsNullOrWhiteSpace(config.ApiKey) || !IsApiKey(config.ApiKey))
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = false,
                    Description = "OpenAI API key missing. OpenCode session usage is tracked under Codex.",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
                }
            };
        }

        return await GetApiKeyUsageAsync(config.ApiKey);
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
        var resetSeconds = ReadDouble(doc.RootElement, "rate_limit", "primary_window", "reset_after_seconds");
        var remaining = Math.Clamp(100.0 - used, 0.0, 100.0);

        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "OpenAI",
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            RequestsPercentage = remaining,
            RequestsUsed = used,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            Description = $"{remaining:F0}% remaining ({used:F0}% used) | Plan: {planType}",
            AuthSource = "OpenCode Session",
            NextResetTime = resetSeconds.HasValue && resetSeconds.Value > 0
                ? DateTime.UtcNow.AddSeconds(resetSeconds.Value).ToLocalTime()
                : null,
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

        if (used.HasValue)
        {
            details.Add(new ProviderUsageDetail
            {
                Name = "Primary Window",
                Used = $"{used.Value:F0}% used",
                Description = reset.HasValue && reset.Value > 0 ? $"Resets in {(int)reset.Value}s" : string.Empty
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

    private sealed class OpenCodeOpenAiAuth
    {
        public string? Access { get; set; }
        public string? AccountId { get; set; }
    }
}

