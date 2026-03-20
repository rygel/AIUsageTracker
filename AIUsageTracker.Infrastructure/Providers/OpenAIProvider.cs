// <copyright file="OpenAIProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Helpers;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Paths;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Constants;
using AIUsageTracker.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenAIProvider : ProviderBase
{
    private const string WhamUsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";

    private readonly IResilientHttpClient _resilientHttpClient;
    private readonly ILogger<OpenAIProvider> _logger;

    public OpenAIProvider(IResilientHttpClient resilientHttpClient, IProviderDiscoveryService discoveryService, ILogger<OpenAIProvider> logger)
        : base(discoveryService)
    {
        this._resilientHttpClient = resilientHttpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "openai",
        "OpenAI (API)",
        PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based")
    {
        DiscoveryEnvironmentVariables = new[] { "OPENAI_API_KEY" },
        RooConfigPropertyNames = new[] { "openAiApiKey" },
        ExplicitApiKeyPrefixes = new[] { "sk-" },
        SessionAuthCanonicalProviderId = "codex",
        SessionAuthMigrationDescription = "Migrated from OpenAI session config",
        SettingsMode = ProviderSettingsMode.SessionAuthStatus,
        UseSessionAuthStatusWhenQuotaBasedOrSessionToken = true,
        SessionStatusLabel = "OpenAI (API)",
        ShowInMainWindow = false,
        SessionIdentitySource = ProviderSessionIdentitySource.OpenAi,
        SupportsAccountIdentity = true,
        ShowInSettings = false,
        IconAssetName = "openai",
        BadgeColorHex = "#008B8B",
        BadgeInitial = "AI",
        AuthIdentityCandidatePathTemplates = new[]
        {
            "%USERPROFILE%\\.local\\share\\opencode\\auth.json",
            "%APPDATA%\\opencode\\auth.json",
            "%LOCALAPPDATA%\\opencode\\auth.json",
            "%USERPROFILE%\\.opencode\\auth.json",
        },
        SessionAuthFileSchemas = new[]
        {
            new ProviderAuthFileSchema("openai", "access", "accountId", "id_token"),
        },
        SessionIdentityProfileRootProperties = new[]
        {
            ProviderEndpoints.OpenAI.ProfileClaimKey,
        },
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Burst,   "5h",     PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.Rolling, "Weekly", PeriodDuration: TimeSpan.FromDays(7)),
        },
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (!string.IsNullOrWhiteSpace(config.ApiKey) && IsApiKey(config.ApiKey))
        {
            return await this.GetApiKeyUsageAsync(config.ApiKey).ConfigureAwait(false);
        }

        var accessToken = config.ApiKey;
        string? accountId = null;

        if (string.IsNullOrWhiteSpace(accessToken) && this.DiscoveryService != null)
        {
            var auth = await this.DiscoveryService.DiscoverAuthAsync(this.Definition.CreateAuthDiscoverySpec()).ConfigureAwait(false);
            accessToken = auth?.AccessToken;
            accountId = auth?.AccountId;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new[]
            {
                this.CreateUnavailableUsage("OpenAI API key or OpenCode session not found.", state: ProviderUsageState.Missing),
            };
        }

        try
        {
            return new[] { await this.GetNativeUsageAsync(accessToken, accountId).ConfigureAwait(false) };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "OpenAI session check failed");
            return new[] { this.CreateUnavailableUsageFromException(ex) };
        }
    }

    private static bool IsApiKey(string token)
    {
        return token.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ProviderUsageDetail> BuildOpenAiSessionDetails(JsonElement root)
    {
        var details = new List<ProviderUsageDetail>();
        var used = root.ReadDouble("rate_limit", "primary_window", "used_percent");
        var reset = root.ReadDouble("rate_limit", "primary_window", "reset_after_seconds");
        var primaryResetTime = ResolveWindowResetTime(root, "primary_window");

        // Emit the Burst detail when usage OR reset timer is present. When the window just reset
        // the API may omit used_percent (returning only reset_after_seconds), so a used-only guard
        // would silently drop the bar, preventing dual-bar rendering on the parent card.
        if (used.HasValue || primaryResetTime.HasValue)
        {
            var primaryRemaining = Math.Clamp(100.0 - (used ?? 0.0), 0.0, 100.0);
            details.Add(new ProviderUsageDetail
            {
                Name = "5-hour quota",
                Description = FormatResetDescription(reset),
                NextResetTime = primaryResetTime,
                DetailType = ProviderUsageDetailType.QuotaWindow,
                QuotaBucketKind = WindowKind.Burst,
                PercentageValue = primaryRemaining,
                PercentageSemantic = PercentageValueSemantic.Remaining,
            });
        }

        var weeklyUsed = root.ReadDouble("rate_limit", "secondary_window", "used_percent");
        var weeklyReset = root.ReadDouble("rate_limit", "secondary_window", "reset_after_seconds");
        var weeklyResetTime = ResolveWindowResetTime(root, "secondary_window");
        if (weeklyUsed.HasValue || weeklyResetTime.HasValue)
        {
            var secondaryRemaining = Math.Clamp(100.0 - (weeklyUsed ?? 0.0), 0.0, 100.0);
            details.Add(new ProviderUsageDetail
            {
                Name = "Weekly quota",
                Description = FormatResetDescription(weeklyReset),
                NextResetTime = weeklyResetTime,
                DetailType = ProviderUsageDetailType.QuotaWindow,
                QuotaBucketKind = WindowKind.Rolling,
                PercentageValue = secondaryRemaining,
                PercentageSemantic = PercentageValueSemantic.Remaining,
            });
        }

        var credits = root.ReadDouble("credits", "balance");
        var unlimited = root.ReadBool("credits", "unlimited");
        if (credits.HasValue || unlimited.HasValue)
        {
            details.Add(new ProviderUsageDetail
            {
                Name = "Credits",
                Description = unlimited == true ? "Unlimited" : credits?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown",
                DetailType = ProviderUsageDetailType.Credit,
                QuotaBucketKind = WindowKind.None,
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
        var resetSeconds = root.ReadDouble("rate_limit", windowName, "reset_after_seconds")
                          ?? root.ReadDouble("rate_limit", windowName, "reset_after");

        var resetFromSeconds = ResolveResetTimeFromSeconds(resetSeconds);
        if (resetFromSeconds.HasValue)
        {
            return resetFromSeconds;
        }

        var resetAtIso = root.ReadString("rate_limit", windowName, "resets_at")
                         ?? root.ReadString("rate_limit", windowName, "reset_at");

        if (!string.IsNullOrWhiteSpace(resetAtIso) &&
            DateTime.TryParse(resetAtIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedResetAt))
        {
            return parsedResetAt.ToLocalTime();
        }

        var resetAtEpoch = root.ReadDouble("rate_limit", windowName, "reset_at_unix");
        if (resetAtEpoch.HasValue && resetAtEpoch.Value > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)resetAtEpoch.Value).LocalDateTime;
        }

        return null;
    }

    private static string? GetAccountIdentity(JsonElement root, string accessToken, string? accountId)
    {
        var directIdentity = SessionIdentityHelper.TryGetPreferredIdentity(root, StaticDefinition.SessionIdentityProfileRootProperties);
        if (!string.IsNullOrWhiteSpace(directIdentity))
        {
            return directIdentity;
        }

        var fromToken = SessionIdentityHelper.TryGetIdentityFromJwt(accessToken, StaticDefinition.SessionIdentityProfileRootProperties);
        if (!string.IsNullOrWhiteSpace(fromToken))
        {
            return fromToken;
        }

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            return accountId;
        }

        return null;
    }

    private async Task<IEnumerable<ProviderUsage>> GetApiKeyUsageAsync(string apiKey)
    {
        if (apiKey.StartsWith("sk-proj", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    IsAvailable = false,
                    State = ProviderUsageState.Missing,
                    Description = "Project keys (sk-proj-...) not supported yet. Use a standard user API key.",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                },
            };
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var response = await this._resilientHttpClient.SendAsync(request, this.ProviderId).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = this.ProviderId,
                        ProviderName = this.Definition.DisplayName,
                        IsAvailable = true,
                        UsedPercent = 0,
                        IsQuotaBased = true,
                        PlanType = PlanType.Coding,
                        Description = "Connected (API Key)",
                        IsStatusOnly = true,
                    },
                };
            }

            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    IsAvailable = false,
                    State = ProviderUsageState.Error,
                    Description = $"Invalid Key ({response.StatusCode})",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                },
            };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "OpenAI API key validation failed");
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    IsAvailable = false,
                    State = ProviderUsageState.Error,
                    Description = "Connection Failed",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                },
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

        using var response = await this._resilientHttpClient.SendAsync(request, this.ProviderId).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return this.CreateUnavailableUsage($"Session invalid ({(int)response.StatusCode})", (int)response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            return this.CreateUnavailableUsage($"Session usage request failed ({(int)response.StatusCode})", (int)response.StatusCode);
        }

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
        {
            return this.CreateUnavailableUsage(detail.GetString() ?? "Session usage request failed", (int)response.StatusCode);
        }

        var planType = doc.RootElement.ReadString("plan_type") ?? "chatgpt";
        var primaryUsed = doc.RootElement.ReadDouble("rate_limit", "primary_window", "used_percent") ?? 0.0;
        var secondaryUsed = doc.RootElement.ReadDouble("rate_limit", "secondary_window", "used_percent") ?? 0.0;
        var nextResetTime = ResolveResetTime(doc.RootElement);

        // Use the higher of primary or secondary window usage for the main display.
        // The API may return 0 for primary_window but have actual usage in secondary_window.
        var used = Math.Max(primaryUsed, secondaryUsed);
        var remaining = Math.Clamp(100.0 - used, 0.0, 100.0);

        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = this.Definition.DisplayName,
            AccountName = GetAccountIdentity(doc.RootElement, accessToken, accountId) ?? string.Empty,
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsedPercent = used,
            RequestsUsed = used,
            RequestsAvailable = 100,
            Description = $"{remaining:F0}% remaining ({used:F0}% used) | Plan: {planType}",
            AuthSource = AuthSource.OpenCodeSession,
            NextResetTime = nextResetTime,
            Details = BuildOpenAiSessionDetails(doc.RootElement),
            RawJson = content,
            HttpStatus = (int)response.StatusCode,
        };
    }
}
