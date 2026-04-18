// <copyright file="OpenAIProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageTracker.Core.Helpers;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Constants;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenAIProvider : ProviderBase
{
    private const string WhamUsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private const string ModelsEndpoint = "https://api.openai.com/v1/models";
    private const string JsonKeyRateLimit = "rate_limit";
    private const string JsonKeyPrimaryWindow = "primary_window";
    private const string JsonKeySecondaryWindow = "secondary_window";
    private const string JsonKeyUsedPercent = "used_percent";
    private const string JsonKeyResetAfterSeconds = "reset_after_seconds";

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProvider> _logger;

    public OpenAIProvider(HttpClient httpClient, IProviderDiscoveryService discoveryService, ILogger<OpenAIProvider> logger)
        : base(discoveryService)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "openai",
        "OpenAI (API)",
        PlanType.Coding,
        isQuotaBased: true)
    {
        DiscoveryEnvironmentVariables = new[] { "OPENAI_API_KEY" },
        RooConfigPropertyNames = new[] { "openAiApiKey" },
        ExplicitApiKeyPrefixes = new[] { "sk-" },
        NonPersistedProviderIds = new[] { "openai" },
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
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        if (!string.IsNullOrWhiteSpace(config.ApiKey) && IsApiKey(config.ApiKey))
        {
            return await this.GetApiKeyUsageAsync(config.ApiKey, providerLabel).ConfigureAwait(false);
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
            return await this.GetNativeUsageAsync(accessToken, accountId, providerLabel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogError(ex, "OpenAI session check failed");
            return new[] { this.CreateUnavailableUsage(DescribeUnavailableException(ex)) };
        }
    }

    private static bool IsApiKey(string token)
    {
        return token.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static (DateTime? BurstResetTime, double? BurstUsed, string BurstDesc, DateTime? WeeklyResetTime, double? WeeklyUsed, string WeeklyDesc, string? CreditsDesc) ParseOpenAiSessionWindows(JsonElement root)
    {
        var primaryUsed = root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyUsedPercent);
        var primaryReset = root.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyResetAfterSeconds);
        var primaryResetTime = ResolveWindowResetTime(root, JsonKeyPrimaryWindow);

        DateTime? burstResetTime = null;
        double? burstUsed = null;
        string burstDesc = string.Empty;

        if (primaryUsed.HasValue || primaryResetTime.HasValue)
        {
            burstUsed = Math.Clamp(primaryUsed ?? 0.0, 0.0, 100.0);
            burstDesc = FormatResetDescription(primaryReset);
            burstResetTime = primaryResetTime;
        }

        var weeklyUsedVal = root.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyUsedPercent);
        var weeklyReset = root.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyResetAfterSeconds);
        var weeklyResetTime = ResolveWindowResetTime(root, JsonKeySecondaryWindow);

        DateTime? weeklyResetTimeParsed = null;
        double? weeklyUsed = null;
        string weeklyDesc = string.Empty;

        if (weeklyUsedVal.HasValue || weeklyResetTime.HasValue)
        {
            weeklyUsed = Math.Clamp(weeklyUsedVal ?? 0.0, 0.0, 100.0);
            weeklyDesc = FormatResetDescription(weeklyReset);
            weeklyResetTimeParsed = weeklyResetTime;
        }

        var credits = root.ReadDouble("credits", "balance");
        var unlimited = root.ReadBool("credits", "unlimited");
        string? creditsDesc = null;
        if (credits.HasValue || unlimited.HasValue)
        {
            creditsDesc = unlimited == true ? "Unlimited" : credits?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";
        }

        return (burstResetTime, burstUsed, burstDesc, weeklyResetTimeParsed, weeklyUsed, weeklyDesc, creditsDesc);
    }

    private static DateTime? ResolveResetTime(JsonElement root)
    {
        var primaryReset = ResolveWindowResetTime(root, JsonKeyPrimaryWindow);
        if (primaryReset.HasValue)
        {
            return primaryReset;
        }

        return ResolveWindowResetTime(root, JsonKeySecondaryWindow);
    }

    private static DateTime? ResolveWindowResetTime(JsonElement root, string windowName)
    {
        var resetSeconds = root.ReadDouble(JsonKeyRateLimit, windowName, JsonKeyResetAfterSeconds)
                          ?? root.ReadDouble(JsonKeyRateLimit, windowName, "reset_after");

        var resetFromSeconds = ResolveResetTimeFromSeconds(resetSeconds);
        if (resetFromSeconds.HasValue)
        {
            return resetFromSeconds;
        }

        var resetAtIso = root.ReadString(JsonKeyRateLimit, windowName, "resets_at")
                         ?? root.ReadString(JsonKeyRateLimit, windowName, "reset_at");

        if (!string.IsNullOrWhiteSpace(resetAtIso) &&
            DateTime.TryParse(resetAtIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedResetAt))
        {
            return parsedResetAt.ToLocalTime();
        }

        var resetAtEpoch = root.ReadDouble(JsonKeyRateLimit, windowName, "reset_at_unix");
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

    private async Task<IEnumerable<ProviderUsage>> GetApiKeyUsageAsync(string apiKey, string providerLabel)
    {
        if (apiKey.StartsWith("sk-proj", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    State = ProviderUsageState.Missing,
                    Description = "Project keys (sk-proj-...) not supported yet. Use a standard user API key.",
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                },
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = this.ProviderId,
                        ProviderName = providerLabel,
                        IsAvailable = true,
                        UsedPercent = 0,
                        IsQuotaBased = this.Definition.IsQuotaBased,
                        PlanType = this.Definition.PlanType,
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
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    State = ProviderUsageState.Error,
                    Description = $"Invalid Key ({response.StatusCode})",
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                },
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogError(ex, "OpenAI API key validation failed");
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    State = ProviderUsageState.Error,
                    Description = "Connection Failed",
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                },
            };
        }
    }

    private async Task<IEnumerable<ProviderUsage>> GetNativeUsageAsync(string accessToken, string? accountId, string providerLabel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WhamUsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
        }

        using var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new[] { this.CreateUnavailableUsage($"Session invalid ({((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)})", (int)response.StatusCode) };
        }

        if (!response.IsSuccessStatusCode)
        {
            return new[] { this.CreateUnavailableUsage($"Session usage request failed ({((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)})", (int)response.StatusCode) };
        }

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
        {
            return new[] { this.CreateUnavailableUsage(detail.GetString() ?? "Session usage request failed", (int)response.StatusCode) };
        }

        var planType = doc.RootElement.ReadString("plan_type") ?? "chatgpt";
        var accountIdentity = GetAccountIdentity(doc.RootElement, accessToken, accountId) ?? string.Empty;
        var httpStatus = (int)response.StatusCode;
        var (burstResetTime, burstUsed, burstDesc, weeklyResetTime, weeklyUsed, weeklyDesc, creditsDescRaw) = ParseOpenAiSessionWindows(doc.RootElement);

        var results = new List<ProviderUsage>();

        var creditsDesc = creditsDescRaw != null
            ? $" | Credits: {creditsDescRaw}"
            : string.Empty;

        if (burstUsed.HasValue || burstResetTime.HasValue)
        {
            var primaryUsed = burstUsed ?? 0.0;
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                CardId = "burst",
                GroupId = this.ProviderId,
                Name = "5-hour quota",
                AccountName = accountIdentity,
                IsAvailable = true,
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                UsedPercent = primaryUsed,
                RequestsUsed = primaryUsed,
                RequestsAvailable = 100,
                Description = $"{burstDesc} | Plan: {planType}{creditsDesc}",
                AuthSource = AuthSource.OpenCodeSession,
                NextResetTime = burstResetTime,
                PeriodDuration = TimeSpan.FromHours(5),
                WindowKind = WindowKind.Burst,
                RawJson = content,
                HttpStatus = httpStatus,
            });
        }

        if (weeklyUsed.HasValue || weeklyResetTime.HasValue)
        {
            var wUsed = weeklyUsed ?? 0.0;
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                CardId = "weekly",
                GroupId = this.ProviderId,
                Name = "Weekly quota",
                AccountName = accountIdentity,
                IsAvailable = true,
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                UsedPercent = wUsed,
                RequestsUsed = wUsed,
                RequestsAvailable = 100,
                Description = $"{weeklyDesc} | Plan: {planType}{creditsDesc}",
                AuthSource = AuthSource.OpenCodeSession,
                NextResetTime = weeklyResetTime,
                PeriodDuration = TimeSpan.FromDays(7),
                WindowKind = WindowKind.Rolling,
                RawJson = content,
                HttpStatus = httpStatus,
            });
        }

        if (results.Count == 0)
        {
            var primaryUsed = doc.RootElement.ReadDouble(JsonKeyRateLimit, JsonKeyPrimaryWindow, JsonKeyUsedPercent) ?? 0.0;
            var secondaryUsed = doc.RootElement.ReadDouble(JsonKeyRateLimit, JsonKeySecondaryWindow, JsonKeyUsedPercent) ?? 0.0;
            var used = Math.Max(primaryUsed, secondaryUsed);
            var remaining = Math.Clamp(100.0 - used, 0.0, 100.0);
            results.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                AccountName = accountIdentity,
                IsAvailable = true,
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                UsedPercent = used,
                RequestsUsed = used,
                RequestsAvailable = 100,
                Description = $"{remaining.ToString("F0", CultureInfo.InvariantCulture)}% remaining ({used.ToString("F0", CultureInfo.InvariantCulture)}% used) | Plan: {planType}{creditsDesc}",
                AuthSource = AuthSource.OpenCodeSession,
                NextResetTime = ResolveResetTime(doc.RootElement),
                RawJson = content,
                HttpStatus = httpStatus,
            });
        }

        return results;
    }
}
