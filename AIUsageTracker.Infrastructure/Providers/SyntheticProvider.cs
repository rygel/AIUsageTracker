// <copyright file="SyntheticProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public sealed class SyntheticProvider : ProviderBase
{
    private const string DefaultQuotaEndpoint = "https://api.synthetic.new/v2/quotas";

    private readonly HttpClient _httpClient;
    private readonly ILogger<SyntheticProvider> _logger;

    public SyntheticProvider(HttpClient httpClient, ILogger<SyntheticProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "synthetic",
        "Synthetic.new",
        PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based")
    {
        DiscoveryEnvironmentVariables = new[] { "SYNTHETIC_API_KEY" },
        RooConfigPropertyNames = new[] { "syntheticApiKey" },
        IconAssetName = "synthetic",
        BadgeColorHex = "#FFD700",
        BadgeInitial = "Sy",
    };

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return new[] { this.CreateUnavailableUsage("API Key missing", 401, config.AuthSource, state: ProviderUsageState.Missing) };
        }

        var endpoint = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? DefaultQuotaEndpoint
            : NormalizeEndpoint(config.BaseUrl);

        try
        {
            using var request = CreateBearerRequest(HttpMethod.Get, endpoint, config.ApiKey);

            using var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogDebug("Synthetic quota request failed with status code {StatusCode}", response.StatusCode);
                return new[] { this.CreateUnavailableUsage($"API Error: {response.StatusCode}", (int)response.StatusCode, config.AuthSource) };
            }

            if (content.Trim().Equals("Not Found", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { this.CreateUnavailableUsage("Endpoint returned 'Not Found' - check API key", (int)response.StatusCode, config.AuthSource) };
            }

            using var document = JsonDocument.Parse(content);
            if (!TryResolveUsage(document.RootElement, out var total, out var used, out var resetRaw))
            {
                return new[] { this.CreateUnavailableUsage("Response missing quota fields (total/used/reset)", (int)response.StatusCode, config.AuthSource) };
            }

            var remainingPercent = Math.Clamp(((total - used) / total) * 100.0, 0, 100);
            var resetLabel = BuildResetLabel(resetRaw, out var nextResetTime);

            var usedLabel = used == Math.Truncate(used)
                ? ((int)used).ToString(CultureInfo.InvariantCulture)
                : used.ToString("F2", CultureInfo.InvariantCulture);
            var totalLabel = total == Math.Truncate(total)
                ? ((int)total).ToString(CultureInfo.InvariantCulture)
                : total.ToString("F2", CultureInfo.InvariantCulture);

            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = "Synthetic.new",
                    UsedPercent = Math.Clamp(used / total * 100.0, 0, 100),
                    RequestsUsed = used,
                    RequestsAvailable = total,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    IsAvailable = true,
                    Description = $"{usedLabel} / {totalLabel} credits{resetLabel}",
                    NextResetTime = nextResetTime,
                    AuthSource = config.AuthSource ?? string.Empty,
                    RawJson = content,
                    HttpStatus = (int)response.StatusCode,
                },
            };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Synthetic provider check failed");
            return new[] { this.CreateUnavailableUsageFromException(ex, authSource: config.AuthSource) };
        }
    }

    private static string NormalizeEndpoint(string baseUrl)
    {
        var url = baseUrl.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://{url}";
        }

        if (url.Contains("/quota", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (url.EndsWith("/v2", StringComparison.OrdinalIgnoreCase))
        {
            return $"{url}/quotas";
        }

        if (url.EndsWith("/v2/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{url}quotas";
        }

        return $"{url.TrimEnd('/')}/v2/quotas";
    }

    private static bool TryResolveUsage(
        JsonElement root,
        out double total,
        out double used,
        out string? resetRaw)
    {
        total = 0;
        used = 0;
        resetRaw = null;

        if (!TryGetSubscriptionElement(root, out var subscription))
        {
            return false;
        }

        if (!TryGetDoubleCandidate(
                subscription,
                out total,
                "limit",
                "quota",
                "total",
                "total_quota",
                "total_requests",
                "request_limit",
                "max_requests",
                "maxRequests"))
        {
            total = 0;
        }

        if (!TryGetDoubleCandidate(
                subscription,
                out used,
                "requests",
                "requests_used",
                "used_requests",
                "usage",
                "used",
                "consumed",
                "spent",
                "used_quota",
                "usedQuota"))
        {
            used = 0;
        }

        // Some payloads expose remaining only; infer used when possible.
        if (total > 0 &&
            TryGetDoubleCandidate(
                subscription,
                out var remaining,
                "remaining",
                "remaining_requests",
                "remaining_quota",
                "available",
                "available_quota",
                "left"))
        {
            var inferredUsed = total - remaining;
            if (inferredUsed >= 0 && (used <= 0 || inferredUsed > used))
            {
                used = inferredUsed;
            }
        }

        resetRaw = TryGetStringCandidate(
            subscription,
            "renewsAt",
            "renews_at",
            "resetAt",
            "reset_at",
            "nextResetAt",
            "next_reset_at");

        return total > 0;
    }

    private static bool TryGetSubscriptionElement(JsonElement root, out JsonElement subscription)
    {
        subscription = default;

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropertyIgnoreCase(root, "subscription", out var directSubscription) &&
                directSubscription.ValueKind == JsonValueKind.Object)
            {
                subscription = directSubscription;
                return true;
            }

            // Some responses wrap subscription under "data".
            if (TryGetPropertyIgnoreCase(root, "data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                TryGetPropertyIgnoreCase(data, "subscription", out var nestedSubscription) &&
                nestedSubscription.ValueKind == JsonValueKind.Object)
            {
                subscription = nestedSubscription;
                return true;
            }

            // Allow flat payloads where usage fields are on root.
            subscription = root;
            return true;
        }

        return false;
    }

    private static bool TryGetDoubleCandidate(JsonElement source, out double value, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (TryGetDoubleProperty(source, candidate, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetDoubleProperty(JsonElement source, string propertyName, out double value)
    {
        value = 0;
        if (source.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(source, propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetDouble(out value);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    private static string? TryGetStringCandidate(JsonElement source, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (source.ValueKind != JsonValueKind.Object ||
                !TryGetPropertyIgnoreCase(source, candidate, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement source, string propertyName, out JsonElement property)
    {
        if (source.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in source.EnumerateObject())
            {
                if (candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static string BuildResetLabel(string? resetRaw, out DateTime? nextResetTime)
    {
        nextResetTime = null;

        if (string.IsNullOrWhiteSpace(resetRaw) || !DateTime.TryParse(resetRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return string.Empty;
        }

        var localTime = parsed.ToUniversalTime().ToLocalTime();
        nextResetTime = localTime;
        return $" (Resets: {localTime:MMM dd HH:mm})";
    }
}
